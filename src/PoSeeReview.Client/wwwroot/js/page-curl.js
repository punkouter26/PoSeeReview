// "Hot off the press": a printed copy of the comic peels off the page and flies to wherever it
// was just sent, and the original is stamped CERTIFIED STRANGE.
//
// THE MOMENT THIS IS FOR. Share and Save are the two actions the whole product exists to earn,
// and both used to end in a toast. A toast says "done"; it does not say "you made a thing and it
// went somewhere". Peeling a copy off the comic and throwing it at the button you pressed makes
// the abstract action physical, and the stamp lands on the original so the comic you are still
// looking at carries the mark of having been sent.
//
// HOW THE PEEL WORKS. The copy is a 64x64 plane laid exactly over the <img>'s rect, deformed in
// the vertex shader by a cylinder whose axis sweeps from the bottom-right corner toward the top-
// left — the classic page curl, so the corner rolls up and over rather than hinging. The real
// comic stays underneath the whole time; the copy is an identical texture, so while it is flat it
// is invisible, and where it lifts the original shows through, which is exactly what a print
// coming off a plate looks like.
//
// COST DECISIONS.
//   * No depth buffer. The cylinder model makes height a monotonic function of distance past the
//     fold, and the fold direction is fixed for the whole effect, so the triangles are sorted ONCE
//     by that distance and drawn back to front. Painter's order is exact here and costs nothing.
//   * The cast shadow is the same mesh flattened onto the page along the light direction, blended
//     with MAX so overlapping layers of the curl do not double-darken. The occlusion the rolled
//     layer casts on the still-flat part of the copy is analytic (a distance from the fold).
//   * One fixed full-viewport canvas, DPR capped at 1.5, alive for ~1.4s, then removed. Nothing
//     persists between shares.
//   * The stamp is DOM, not GPU: it must survive on the `lite` tier, it sits on the real comic
//     (so it scrolls with it), and it is two spans of text that the browser already renders
//     better than a texture could. Its scale and opacity are written from the shared loop rather
//     than a CSS animation so there is one clock for the whole moment.
//
// FALLBACKS. `off` (reduced motion / no WebGL2) runs nothing. `lite`, a failed image load, a
// failed shader or a lost context all fall back to the stamp alone. Dropping to `lite` mid-peel
// removes the copy (the original was never hidden); dropping to `off` or the comic being
// detached by a navigation ends everything. Every exit removes the canvas, the stamp, the
// splatter and any positioning this module put on the stamp's host.

import { gfx, createSurface, compileProgram, loadImage, createTexture } from './gfx-core.js';

const GRID = 64;

const PEEL_MS = 980;            // fold sweep
const FLY_START_MS = 620;       // lift-off overlaps the end of the sweep so the motion never stalls
const FLY_MS = 700;
const STAMP_AT_MS = 1000;       // impact, as the copy leaves
const STAMP_ONLY_AT_MS = 170;   // impact when there is no peel to wait for
const STAMP_FALL_MS = 150;
const STAMP_HOLD_MS = 1200;
const STAMP_FADE_MS = 320;
const SPRAY_MS = 240;
const STAMP_START_SCALE = 2.4;
const STAMP_ROTATE_RAD = -12 * Math.PI / 180;
const IMAGE_TIMEOUT_MS = 1200;  // the same-origin copy was normally just fetched for Save/Share

// Fold travel direction in sheet pixels (y down): from the bottom-right corner toward the top-
// left, flatter than the diagonal so the fold enters across the bottom edge first.
const FOLD_DIR = normalize2(-1, -0.72);
const AXIS_MAX_FRACTION = 0.6;  // of the sheet's extent along FOLD_DIR
const EYE_DISTANCE = 1500;      // px; perspective for the lift toward the viewer
const LIGHT = normalize3(-0.35, -0.55, 0.85);   // from the top-left, toward the viewer

const instances = new Map();
let nextId = 1;

const SHEET_VS = `#version 300 es
precision highp float;
layout(location = 0) in vec2 aUv;
uniform vec2  uViewport;
uniform vec2  uOrigin;
uniform vec2  uSize;
uniform vec2  uDir;
uniform float uAxis;
uniform float uRadius;
uniform vec2  uFlyOffset;
uniform float uFlyScale;
uniform float uFlySpin;
uniform float uFlyLift;
uniform float uProjectShadow;
uniform vec3  uLight;
uniform float uEye;
out vec2  vUv;
out vec2  vLocal;
out vec3  vNormal;
out float vHeight;
out float vFold;
const float PI = 3.14159265;

void main() {
    vec2 local = aUv * uSize;
    // Distance along the fold direction from the bottom-right corner; everything closer to the
    // corner than the axis has been rolled onto the cylinder.
    float s = dot(local - uSize, uDir);
    float x = uAxis - s;
    vec3 p = vec3(local, 0.0);
    vec3 n = vec3(0.0, 0.0, 1.0);
    if (x > 0.0 && uRadius > 0.0) {
        float theta = x / uRadius;
        float moved;
        if (theta < PI) {
            moved = uAxis - uRadius * sin(theta);
            p.z = uRadius * (1.0 - cos(theta));
            n = vec3(uDir * sin(theta), cos(theta));
        } else {
            // Past the half turn the sheet lies face-down on top of itself.
            moved = uAxis + (x - PI * uRadius);
            p.z = 2.0 * uRadius;
            n = vec3(0.0, 0.0, -1.0);
        }
        p.xy = local + uDir * (moved - s);
    }

    vec2 c = uSize * 0.5;
    float cs = cos(uFlySpin);
    float sn = sin(uFlySpin);
    mat2 rot = mat2(cs, sn, -sn, cs);
    vec3 world = vec3(uOrigin + c + uFlyOffset + rot * (p.xy - c) * uFlyScale, p.z * uFlyScale + uFlyLift);
    n.xy = rot * n.xy;
    vHeight = world.z;

    if (uProjectShadow > 0.5) {
        world.xy -= uLight.xy / uLight.z * world.z;
        world.z = 0.0;
    }

    float k = uEye / max(uEye - world.z, 1.0);
    vec2 screen = uViewport * 0.5 + (world.xy - uViewport * 0.5) * k;
    gl_Position = vec4(screen.x / uViewport.x * 2.0 - 1.0, 1.0 - screen.y / uViewport.y * 2.0, 0.0, 1.0);

    vUv = aUv;
    vLocal = local;
    vNormal = n;
    vFold = x;
}`;

const SHEET_FS = `#version 300 es
precision highp float;
in vec2  vUv;
in vec2  vLocal;
in vec3  vNormal;
in float vHeight;
in float vFold;
uniform sampler2D uTex;
uniform vec2  uSize;
uniform float uCorner;
uniform vec3  uLight;
uniform vec3  uPaper;
uniform float uRadius;
uniform float uAxis;
uniform float uAlpha;
uniform float uGlint;
uniform float uDark;
out vec4 outColor;
const float PI = 3.14159265;

float hash(vec2 p) {
    p = fract(p * vec2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return fract(p.x * p.y);
}

float roundedRect(vec2 p, vec2 size, float r) {
    vec2 h = size * 0.5;
    vec2 d = abs(p - h) - (h - r);
    return length(max(d, 0.0)) + min(max(d.x, d.y), 0.0) - r;
}

void main() {
    // The container clips the real comic to a rounded rect; the copy matches, antialiased inward.
    float sdf = roundedRect(vLocal, uSize, uCorner);
    float cover = clamp(-sdf / max(fwidth(sdf), 1e-3), 0.0, 1.0);
    if (cover <= 0.0) discard;

    vec3 N = normalize(vNormal);
    vec3 H = normalize(uLight + vec3(0.0, 0.0, 1.0));
    vec3 print = texture(uTex, vec2(vUv.x, 1.0 - vUv.y)).rgb;
    vec3 col;

    if (N.z >= 0.0) {
        // Print side. Lighting is normalised so the flat sheet is exactly the source pixels and
        // only the curl reads as lit.
        float diff = clamp(dot(N, uLight) / uLight.z, 0.0, 1.35);
        float spec = max(pow(max(dot(N, H), 0.0), 36.0) - pow(H.z, 36.0), 0.0);
        col = print * (0.5 + 0.5 * diff) + spec * 0.6;

        // A glint that travels across the copy as it lifts away — the gloss of fresh ink.
        float band = (vUv.x + (1.0 - vUv.y)) * 0.5;
        col += exp(-pow((band - uGlint) * 6.0, 2.0)) * 0.16;

        if (vFold <= 0.0 && uAxis > 0.0) {
            float d = -vFold;
            float crease = (1.0 - smoothstep(0.0, uRadius * 1.6, d)) * 0.16;
            float rolled = max(uAxis - PI * uRadius, 0.0);
            float under = rolled > 0.0
                ? (1.0 - smoothstep(rolled * 0.85, rolled + uRadius * 1.5 + 6.0, d)) * 0.24
                : 0.0;
            col *= 1.0 - max(crease, under);
        }
    } else {
        // Paper side: card stock with grain and fibres, a faint mirrored show-through of the print.
        vec3 Nb = -N;
        float diff = clamp(dot(Nb, uLight) / uLight.z, 0.0, 1.3);
        float spec = pow(max(dot(Nb, H), 0.0), 18.0);
        float grain = hash(floor(vLocal * 1.3)) * 0.6 + hash(floor(vLocal * vec2(0.07, 0.9))) * 0.4;
        vec3 paper = uPaper * (0.94 + 0.08 * grain);
        paper = uDark > 0.5
            ? mix(paper, print, 0.07)
            : paper * mix(vec3(1.0), print, 0.1);
        col = paper * (0.45 + 0.55 * diff) + spec * mix(0.10, 0.06, uDark);
        col *= mix(0.82, 1.0, smoothstep(0.0, 1.6, -sdf));   // the cut edge of the stock
    }

    float a = cover * uAlpha;
    outColor = vec4(col * a, a);
}`;

const SHADOW_FS = `#version 300 es
precision highp float;
in vec2  vLocal;
in float vHeight;
uniform vec2  uSize;
uniform float uShadowAlpha;
uniform float uFlyScale;
out vec4 outColor;

void main() {
    // Softness grows with height. It is measured inward from the sheet's edge in sheet pixels,
    // so it is divided by the flight scale to stay the same on screen as the copy shrinks.
    float soft = min((3.0 + vHeight * 0.3) / max(uFlyScale, 0.05), min(uSize.x, uSize.y) * 0.35);
    vec2 e = min(vLocal, uSize - vLocal);
    float edge = min(e.x, e.y);
    float a = smoothstep(0.0, soft, edge)
            * clamp(vHeight / 18.0, 0.0, 1.0)
            * mix(1.0, 0.45, clamp(vHeight / 320.0, 0.0, 1.0))
            * uShadowAlpha;
    outColor = vec4(0.0, 0.0, 0.0, a);
}`;

const SHEET_UNIFORMS = ['uViewport', 'uOrigin', 'uSize', 'uDir', 'uAxis', 'uRadius', 'uFlyOffset', 'uFlyScale',
    'uFlySpin', 'uFlyLift', 'uProjectShadow', 'uLight', 'uEye', 'uTex', 'uCorner', 'uPaper', 'uAlpha', 'uGlint',
    'uDark', 'uShadowAlpha'];

/**
 * @param {HTMLElement} container the .comic-strip-container
 * @param {{ src?: string, target?: Element, onLift?: () => void, onStamp?: () => void, onDone?: () => void }} options
 *   `src` must be same-origin (the display URL is cross-origin without CORS and would taint WebGL).
 * @returns {Promise<number>} handle, or 0 when nothing ran
 */
export async function peel(container, options = {}) {
    try {
        if (!container?.isConnected || !gfx.allows('lite')) return 0;

        // A second share while the first is still in the air replaces it.
        for (const [id, existing] of instances) {
            if (existing.container === container) stop(id);
        }

        const image = gfx.allows('full') && options.src ? await loadImage(options.src, IMAGE_TIMEOUT_MS) : null;
        if (!container.isConnected || !gfx.allows('lite')) return 0;

        return begin(container, options ?? {}, image);
    } catch {
        return 0;
    }
}

/** Immediate cleanup. Idempotent; does not fire onDone. */
export function stop(handle) {
    try {
        const instance = instances.get(handle);
        if (instance) end(instance, false);
    } catch {
        // Nothing to report: the element is gone either way.
    }
}

function begin(container, options, image) {
    const id = nextId++;
    const imageElement = container.querySelector('img') ?? container;
    const instance = {
        id,
        container,
        imageElement,
        options,
        startedAt: -1,
        stampAt: STAMP_ONLY_AT_MS,
        overlay: null,
        stamp: null,
        lifted: false,
        impacted: false,
        ended: false,
        stopTask: null
    };
    instances.set(id, instance);

    if (image && gfx.allows('full')) {
        instance.overlay = createOverlay(instance, image);
        if (instance.overlay) instance.stampAt = STAMP_AT_MS;
    }

    instance.stopTask = gfx.addTask(`page-curl#${id}`, (now) => frame(instance, now));
    return id;
}

function frame(instance, now) {
    if (instance.ended) return;
    if (!instance.container.isConnected) {
        end(instance, true);
        return;
    }
    if (instance.startedAt < 0) instance.startedAt = now;
    const t = now - instance.startedAt;

    if (instance.overlay) {
        if (!instance.lifted) {
            instance.lifted = true;
            fire(instance.options.onLift);
        }
        let alive = false;
        try {
            alive = renderOverlay(instance, t);
        } catch {
            alive = false;
        }
        if (!alive) releaseOverlay(instance);
    }

    const stampT = t - (instance.stampAt - STAMP_FALL_MS);
    if (stampT >= 0 && !instance.stamp) instance.stamp = createStamp(instance);
    if (instance.stamp) updateStamp(instance, stampT);

    if (stampT >= STAMP_FALL_MS + STAMP_HOLD_MS + STAMP_FADE_MS && !instance.overlay) {
        end(instance, true);
    }
}

function end(instance, notify) {
    if (instance.ended) return;
    instance.ended = true;
    releaseOverlay(instance);
    removeStamp(instance);
    instance.stopTask?.();
    instances.delete(instance.id);
    if (notify) fire(instance.options.onDone);
}

function fire(callback) {
    if (typeof callback !== 'function') return;
    try {
        callback();
    } catch {
        // A sound failing to play must not stall the effect.
    }
}

// ── The copy: WebGL overlay ──────────────────────────────────────────────────────────────

function createOverlay(instance, image) {
    const canvas = document.createElement('canvas');
    canvas.setAttribute('aria-hidden', 'true');
    // Fixed and above everything: the copy has to fly over the header to reach the share button.
    Object.assign(canvas.style, {
        position: 'fixed', left: '0', top: '0', width: '100%', height: '100%',
        pointerEvents: 'none', zIndex: '2147483000'
    });

    let surface = null;
    try {
        document.body.appendChild(canvas);
        surface = createSurface(canvas, { maxDpr: 1.5 });
        if (!surface) throw new Error('no webgl2');
        const gl = surface.gl;

        const sheet = compileProgram(gl, SHEET_VS, SHEET_FS);
        const shadow = compileProgram(gl, SHEET_VS, SHADOW_FS);
        const uniforms = (program) => Object.fromEntries(SHEET_UNIFORMS.map((name) => [name, gl.getUniformLocation(program, name)]));

        const rect = instance.imageElement.getBoundingClientRect();
        const vao = gl.createVertexArray();
        gl.bindVertexArray(vao);
        const vbo = gl.createBuffer();
        gl.bindBuffer(gl.ARRAY_BUFFER, vbo);
        gl.bufferData(gl.ARRAY_BUFFER, gridVertices(), gl.STATIC_DRAW);
        gl.enableVertexAttribArray(0);
        gl.vertexAttribPointer(0, 2, gl.FLOAT, false, 0, 0);
        const ibo = gl.createBuffer();
        const indices = sortedGridIndices(Math.max(1, rect.width), Math.max(1, rect.height));
        gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, ibo);
        gl.bufferData(gl.ELEMENT_ARRAY_BUFFER, indices, gl.STATIC_DRAW);
        gl.bindVertexArray(null);

        const texture = createTexture(gl, image, { mipmaps: true });

        const dark = isDark();
        const overlay = {
            canvas, surface, gl, vao, texture, indexCount: indices.length,
            sheet: { program: sheet, u: uniforms(sheet) },
            shadow: { program: shadow, u: uniforms(shadow) },
            dark,
            paper: readColor('--color-card', dark ? [0.11, 0.1, 0.15] : [1, 1, 1]),
            shadowAlpha: dark ? 0.55 : 0.3,
            corner: parseFloat(getComputedStyle(instance.container).borderTopLeftRadius) || 0,
            onLost: null
        };
        overlay.onLost = (event) => {
            event.preventDefault();
            releaseOverlay(instance);
        };
        canvas.addEventListener('webglcontextlost', overlay.onLost);
        return overlay;
    } catch {
        surface?.release();
        canvas.remove();
        return null;
    }
}

function releaseOverlay(instance) {
    const overlay = instance.overlay;
    if (!overlay) return;
    instance.overlay = null;
    try {
        overlay.canvas.removeEventListener('webglcontextlost', overlay.onLost);
        if (!overlay.gl.isContextLost()) {
            overlay.gl.deleteTexture(overlay.texture);
            overlay.gl.deleteProgram(overlay.sheet.program);
            overlay.gl.deleteProgram(overlay.shadow.program);
            overlay.gl.deleteVertexArray(overlay.vao);
        }
    } catch {
        // Context already gone; release() below is what matters.
    }
    overlay.surface.release();
    overlay.canvas.remove();
}

/** Returns false once the copy has finished its flight (or has nothing left to draw on). */
function renderOverlay(instance, t) {
    const overlay = instance.overlay;
    const fly = clamp01((t - FLY_START_MS) / FLY_MS);
    if (fly >= 1) return false;
    if (!gfx.allows('full')) return false;

    const rect = instance.imageElement.getBoundingClientRect();
    if (rect.width < 2 || rect.height < 2) return false;
    if (!overlay.surface.beginFrame()) return false;

    const gl = overlay.gl;
    const viewW = overlay.canvas.clientWidth || window.innerWidth;
    const viewH = overlay.canvas.clientHeight || window.innerHeight;
    const w = rect.width;
    const h = rect.height;

    // Fold: starts slow as the corner is pried up, then rolls faster as it frees itself.
    const peelT = clamp01(t / PEEL_MS);
    const peelEase = Math.pow(peelT, 1.45);
    const extent = -(w * FOLD_DIR[0] + h * FOLD_DIR[1]);
    const axis = extent * AXIS_MAX_FRACTION * peelEase;
    const radius = Math.min(w, h) * (0.045 + 0.065 * peelEase);

    // Flight: rise toward the viewer first, then get pulled into the target along an arc.
    const start = [rect.left + w / 2, rect.top + h / 2];
    const end = targetPoint(instance.options.target, viewW);
    const rise = easeOutCubic(clamp01(fly / 0.35));
    const travel = easeInOutCubic(clamp01((fly - 0.06) / 0.94));
    const pos = quadBezier(start, arcControl(start, end, w), end, travel);
    const lift = 130 * rise * (1 - travel);
    const scale = 1 - 0.95 * travel;
    const spin = -0.5 * travel + 0.06 * Math.sin(fly * Math.PI);
    const alpha = 1 - smoothstep(0.82, 1, fly);
    const glint = -0.4 + 1.8 * clamp01((t - FLY_START_MS * 0.7) / (FLY_MS * 0.7));

    gl.clearColor(0, 0, 0, 0);
    gl.clear(gl.COLOR_BUFFER_BIT);
    gl.enable(gl.BLEND);
    gl.bindVertexArray(overlay.vao);

    const common = (u) => {
        gl.uniform2f(u.uViewport, viewW, viewH);
        gl.uniform2f(u.uOrigin, rect.left, rect.top);
        gl.uniform2f(u.uSize, w, h);
        gl.uniform2f(u.uDir, FOLD_DIR[0], FOLD_DIR[1]);
        gl.uniform1f(u.uAxis, axis);
        gl.uniform1f(u.uRadius, radius);
        gl.uniform2f(u.uFlyOffset, pos[0] - start[0], pos[1] - start[1]);
        gl.uniform1f(u.uFlyScale, scale);
        gl.uniform1f(u.uFlySpin, spin);
        gl.uniform1f(u.uFlyLift, lift);
        gl.uniform3f(u.uLight, LIGHT[0], LIGHT[1], LIGHT[2]);
        gl.uniform1f(u.uEye, EYE_DISTANCE);
    };

    // Shadow first, on the empty canvas: MAX keeps overlapping curl layers from stacking up.
    const shadow = overlay.shadow;
    gl.useProgram(shadow.program);
    common(shadow.u);
    gl.uniform1f(shadow.u.uProjectShadow, 1);
    gl.uniform1f(shadow.u.uShadowAlpha, overlay.shadowAlpha * alpha);
    gl.blendEquation(gl.MAX);
    gl.blendFunc(gl.ONE, gl.ONE);
    gl.drawElements(gl.TRIANGLES, overlay.indexCount, gl.UNSIGNED_SHORT, 0);

    const sheet = overlay.sheet;
    gl.useProgram(sheet.program);
    common(sheet.u);
    gl.uniform1f(sheet.u.uProjectShadow, 0);
    gl.uniform1f(sheet.u.uCorner, Math.min(overlay.corner, w / 2, h / 2));
    gl.uniform3f(sheet.u.uPaper, overlay.paper[0], overlay.paper[1], overlay.paper[2]);
    gl.uniform1f(sheet.u.uAlpha, alpha);
    gl.uniform1f(sheet.u.uGlint, glint);
    gl.uniform1f(sheet.u.uDark, overlay.dark ? 1 : 0);
    gl.activeTexture(gl.TEXTURE0);
    gl.bindTexture(gl.TEXTURE_2D, overlay.texture);
    gl.uniform1i(sheet.u.uTex, 0);
    gl.blendEquation(gl.FUNC_ADD);
    gl.blendFunc(gl.ONE, gl.ONE_MINUS_SRC_ALPHA);
    gl.drawElements(gl.TRIANGLES, overlay.indexCount, gl.UNSIGNED_SHORT, 0);

    gl.bindVertexArray(null);
    return overlay.surface.present();
}

/**
 * The flight bows out to the side the corner was already travelling (up-left), so the copy keeps
 * the peel's momentum and clears the middle of the comic before the stamp lands there.
 */
function arcControl(start, end, w) {
    const dx = end[0] - start[0];
    const dy = end[1] - start[1];
    const len = Math.hypot(dx, dy) || 1;
    let px = -dy / len;
    let py = dx / len;
    if (px * FOLD_DIR[0] + py * FOLD_DIR[1] < 0) { px = -px; py = -py; }
    const bow = Math.max(len * 0.35, w * 0.3);
    return [start[0] + dx * 0.3 + px * bow, start[1] + dy * 0.3 + py * bow];
}

function targetPoint(target, viewW) {
    try {
        if (target?.isConnected) {
            const r = target.getBoundingClientRect();
            if (r.width > 0 || r.height > 0) return [r.left + r.width / 2, r.top + r.height / 2];
        }
    } catch {
        // Fall through to the corner.
    }
    return [viewW - 56, 56];
}

function gridVertices() {
    const data = new Float32Array((GRID + 1) * (GRID + 1) * 2);
    let i = 0;
    for (let y = 0; y <= GRID; y++) {
        for (let x = 0; x <= GRID; x++) {
            data[i++] = x / GRID;
            data[i++] = y / GRID;
        }
    }
    return data;
}

/**
 * Triangles ordered far-from-the-corner first. Height on the cylinder only ever grows with
 * distance past the fold, so this is a correct back-to-front order for every frame of the curl.
 */
function sortedGridIndices(w, h) {
    const tris = [];
    const row = GRID + 1;
    const s = (u, v) => (u * w - w) * FOLD_DIR[0] + (v * h - h) * FOLD_DIR[1];
    for (let y = 0; y < GRID; y++) {
        for (let x = 0; x < GRID; x++) {
            const a = y * row + x;
            const b = a + 1;
            const c = a + row;
            const d = c + 1;
            const u0 = x / GRID;
            const v0 = y / GRID;
            const step = 1 / GRID;
            tris.push({ key: s(u0 + step / 3, v0 + step / 3), i: [a, c, b] });
            tris.push({ key: s(u0 + step * 2 / 3, v0 + step * 2 / 3), i: [b, c, d] });
        }
    }
    tris.sort((p, q) => q.key - p.key);
    const out = new Uint16Array(tris.length * 3);
    tris.forEach((tri, n) => out.set(tri.i, n * 3));
    return out;
}

// ── The stamp: DOM + 2D splatter ─────────────────────────────────────────────────────────

function createStamp(instance) {
    const container = instance.container;
    const host = container.parentElement;
    if (!host) return null;

    try {
        const stamp = { host, restorePosition: false, el: null, splat: null, ctx: null, drops: [], dpr: 1, ink: '' };
        if (getComputedStyle(host).position === 'static') {
            host.style.position = 'relative';
            stamp.restorePosition = true;
        }

        // Layout offsets, not client rects: the tilt wrapper carries a scroll-driven transform.
        const left = container.offsetLeft;
        const top = container.offsetTop;
        const width = container.offsetWidth;
        const height = container.offsetHeight;

        const el = document.createElement('div');
        el.className = 'fx-stamp';
        el.setAttribute('aria-hidden', 'true');
        el.innerHTML = '<span>Certified</span><span>Strange</span>';
        Object.assign(el.style, { position: 'absolute', left: `${left + width / 2}px`, top: `${top + height / 2}px`, opacity: '0' });
        el.style.setProperty('--fx-stamp-w', `${width}px`);
        host.appendChild(el);
        stamp.el = el;

        // A 2.4x stamp on a full-bleed phone comic would overflow the page sideways for a few
        // frames and nudge the layout viewport, so the starting scale stays inside the viewport.
        const half = (el.offsetWidth * Math.cos(STAMP_ROTATE_RAD) + el.offsetHeight * Math.abs(Math.sin(STAMP_ROTATE_RAD))) / 2;
        const box = container.getBoundingClientRect();
        const centreX = box.left + box.width / 2;
        const room = Math.min(centreX, document.documentElement.clientWidth - centreX) - 4;
        stamp.startScale = half > 0 ? Math.max(1.1, Math.min(STAMP_START_SCALE, room / half)) : STAMP_START_SCALE;
        stamp.size = [el.offsetWidth, el.offsetHeight];

        // Splatter lands on the comic, so its canvas is the comic's box and clips with it.
        const splat = document.createElement('canvas');
        splat.className = 'fx-stamp-splatter';
        splat.setAttribute('aria-hidden', 'true');
        Object.assign(splat.style, {
            position: 'absolute', left: `${left}px`, top: `${top}px`,
            width: `${width}px`, height: `${height}px`, pointerEvents: 'none'
        });
        stamp.dpr = Math.min(window.devicePixelRatio || 1, 2);
        splat.width = Math.max(1, Math.round(width * stamp.dpr));
        splat.height = Math.max(1, Math.round(height * stamp.dpr));
        host.appendChild(splat);
        stamp.splat = splat;
        stamp.ctx = splat.getContext('2d');
        stamp.centre = [width / 2, height / 2];
        stamp.ink = readToken('--color-danger') || 'crimson';
        stamp.sprayDone = false;
        return stamp;
    } catch {
        return null;
    }
}

function updateStamp(instance, stampT) {
    const stamp = instance.stamp;
    if (!stamp?.el) return;

    let scale;
    let opacity;
    if (stampT < STAMP_FALL_MS) {
        // Falling: scale changes fastest at the end, which is what makes the landing hard.
        const u = clamp01(stampT / STAMP_FALL_MS);
        scale = 1 + (stamp.startScale - 1) * (1 - u * u);
        opacity = 0.12 + 0.6 * u;
    } else {
        const d = stampT - STAMP_FALL_MS;
        if (!instance.impacted) {
            instance.impacted = true;
            fire(instance.options.onStamp);
            spawnDroplets(stamp);
        }
        // Rubber compresses on impact and springs back once.
        scale = 1 - 0.06 * Math.exp(-d / 55) * Math.cos(d * 0.055);
        const settle = 1 - 0.06 * smoothstep(0, 240, d);    // wet ink dries a little lighter
        const fade = 1 - smoothstep(STAMP_HOLD_MS, STAMP_HOLD_MS + STAMP_FADE_MS, d);
        opacity = settle * fade;
        drawDroplets(stamp, d);
        if (stamp.splat) stamp.splat.style.opacity = fade.toFixed(3);
    }

    stamp.el.style.scale = scale.toFixed(4);
    stamp.el.style.opacity = opacity.toFixed(3);
}

function spawnDroplets(stamp) {
    const [sw, sh] = stamp.size;
    const cos = Math.cos(STAMP_ROTATE_RAD);
    const sin = Math.sin(STAMP_ROTATE_RAD);
    const rotate = (x, y) => [x * cos - y * sin, x * sin + y * cos];
    const count = 30 + Math.floor(Math.random() * 31);
    const perimeter = 2 * (sw + sh);

    for (let n = 0; n < count; n++) {
        // A point on the stamp's edge, weighted by side length, and that side's outward normal.
        let p = Math.random() * perimeter;
        let lx, ly, nx, ny;
        if (p < sw) { lx = p - sw / 2; ly = -sh / 2; nx = 0; ny = -1; }
        else if ((p -= sw) < sh) { lx = sw / 2; ly = p - sh / 2; nx = 1; ny = 0; }
        else if ((p -= sh) < sw) { lx = sw / 2 - p; ly = sh / 2; nx = 0; ny = 1; }
        else { p -= sw; lx = -sw / 2; ly = sh / 2 - p; nx = -1; ny = 0; }

        const spread = (Math.random() - 0.5) * 1.3;
        const [dx0, dy0] = rotate(nx, ny);
        const dir = [dx0 * Math.cos(spread) - dy0 * Math.sin(spread), dx0 * Math.sin(spread) + dy0 * Math.cos(spread)];
        const [ox, oy] = rotate(lx, ly);
        const flyer = Math.random() < 0.15;
        const blob = !flyer && Math.random() < 0.12;
        const dist = flyer ? 60 + Math.random() * 70 : blob ? 3 + Math.random() * 10 : 5 + Math.pow(Math.random(), 2) * 55;
        const radius = blob ? 3.5 + Math.random() * 3 : flyer ? 1.2 + Math.random() * 1.8 : 1.1 + Math.pow(Math.random(), 1.6) * 3.2;
        const satellites = [];
        if (radius > 2.6) {
            const k = 1 + Math.floor(Math.random() * 3);
            for (let i = 0; i < k; i++) {
                const a = (Math.random() - 0.5) * 1.4;
                satellites.push({
                    dx: (dir[0] * Math.cos(a) - dir[1] * Math.sin(a)) * radius * (1.8 + Math.random() * 2.2),
                    dy: (dir[0] * Math.sin(a) + dir[1] * Math.cos(a)) * radius * (1.8 + Math.random() * 2.2),
                    r: 0.5 + Math.random() * radius * 0.3
                });
            }
        }
        stamp.drops.push({
            origin: [stamp.centre[0] + ox, stamp.centre[1] + oy],
            dir, dist, radius, satellites,
            delay: Math.random() * 40,
            duration: SPRAY_MS * (0.55 + 0.45 * Math.random()),
            smear: 0.08 + Math.random() * 0.22
        });
    }
}

function drawDroplets(stamp, d) {
    const ctx = stamp.ctx;
    if (!ctx || stamp.sprayDone) return;
    ctx.setTransform(stamp.dpr, 0, 0, stamp.dpr, 0, 0);
    ctx.clearRect(0, 0, stamp.splat.width, stamp.splat.height);
    ctx.fillStyle = stamp.ink;

    let landed = true;
    for (const drop of stamp.drops) {
        const raw = (d - drop.delay) / drop.duration;
        if (raw <= 0) { landed = false; continue; }
        if (raw < 1) landed = false;
        const e = easeOutCubic(clamp01(raw));
        const [ox, oy] = drop.origin;
        const [dx, dy] = drop.dir;
        const hx = ox + dx * drop.dist * e;
        const hy = oy + dy * drop.dist * e;
        // The tail is long while the drop is moving and shrinks to a short smear when it lands.
        const tail = Math.min(drop.dist * (drop.smear + (1 - e) * 0.6), drop.radius * (3 + 9 * (1 - e)), drop.dist * e);
        const tx = hx - dx * tail;
        const ty = hy - dy * tail;
        const r = drop.radius * (0.7 + 0.3 * e);
        const px = -dy;
        const py = dx;
        const angle = Math.atan2(py, px);

        ctx.globalAlpha = 0.9;
        ctx.beginPath();
        ctx.moveTo(tx, ty);
        ctx.lineTo(hx + px * r, hy + py * r);
        ctx.arc(hx, hy, r, angle, angle + Math.PI, true);
        ctx.closePath();
        ctx.fill();

        if (e > 0.85) {
            ctx.globalAlpha = 0.85 * smoothstep(0.85, 1, e);
            for (const s of drop.satellites) {
                ctx.beginPath();
                ctx.arc(hx + s.dx, hy + s.dy, s.r, 0, Math.PI * 2);
                ctx.fill();
            }
        }
    }
    ctx.globalAlpha = 1;
    // Once everything has landed the canvas is a still image; stop repainting it.
    if (landed) stamp.sprayDone = true;
}

function removeStamp(instance) {
    const stamp = instance.stamp;
    if (!stamp) return;
    instance.stamp = null;
    try {
        stamp.el?.remove();
        stamp.splat?.remove();
        if (stamp.restorePosition) stamp.host.style.removeProperty('position');
    } catch {
        // Detached along with its host.
    }
}

// ── Small helpers ────────────────────────────────────────────────────────────────────────

function isDark() {
    const theme = document.documentElement.dataset.theme;
    if (theme === 'dark') return true;
    if (theme === 'light') return false;
    try {
        return window.matchMedia('(prefers-color-scheme: dark)').matches;
    } catch {
        return false;
    }
}

function readToken(name) {
    try {
        return getComputedStyle(document.documentElement).getPropertyValue(name).trim();
    } catch {
        return '';
    }
}

function readColor(name, fallback) {
    const m = /^#([0-9a-f]{6})$/i.exec(readToken(name));
    if (!m) return fallback;
    const v = parseInt(m[1], 16);
    return [((v >> 16) & 255) / 255, ((v >> 8) & 255) / 255, (v & 255) / 255];
}

function normalize2(x, y) {
    const l = Math.hypot(x, y);
    return [x / l, y / l];
}

function normalize3(x, y, z) {
    const l = Math.hypot(x, y, z);
    return [x / l, y / l, z / l];
}

function clamp01(v) {
    return v < 0 ? 0 : v > 1 ? 1 : v;
}

function smoothstep(a, b, v) {
    const t = clamp01((v - a) / (b - a));
    return t * t * (3 - 2 * t);
}

function easeOutCubic(t) {
    return 1 - Math.pow(1 - t, 3);
}

function easeInOutCubic(t) {
    return t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2;
}

function quadBezier(a, b, c, t) {
    const u = 1 - t;
    return [u * u * a[0] + 2 * u * t * b[0] + t * t * c[0], u * u * a[1] + 2 * u * t * b[1] + t * t * c[1]];
}

gfx.onTierChanged((tier) => {
    for (const instance of [...instances.values()]) {
        // Below `full` the copy goes (the original was never hidden) and the stamp carries on;
        // at `off` nothing should be moving at all.
        if (tier === 'off') end(instance, true);
        else if (tier !== 'full') releaseOverlay(instance);
    }
});
