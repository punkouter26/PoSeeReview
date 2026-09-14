// Actual refractive glass, as opposed to a blur.
//
// WHAT THIS IS NOT.
//
// `.glass` in app.css is `backdrop-filter: blur()`. That is the only thing CSS can do over DOM
// and it is a genuinely good default, but it is not glass — it is frosting. Real glass bends
// what is behind it, splits the colour at its edges where the bend is steepest, and catches a
// highlight that moves when the light does. None of that is expressible as a blur radius.
//
// WHY IT IS POSSIBLE HERE AND NOT EVERYWHERE.
//
// The usual way to write this is to read the pixels behind the pane and offset the lookup, and
// on the web you cannot: the backdrop is a WebGL canvas, the page above it is DOM, and no
// browser exposes composited DOM to a shader. (comic-fx.js hits the same wall from the other
// direction — the comic blob is cross-origin, so uploading it as a texture taints the canvas and
// the read throws.)
//
// The way through is that the backdrop is PROCEDURAL. gradient.js is a pure function of position
// and time, so a pane does not need to read what is behind it — it can EVALUATE it, at any
// coordinate the refraction asks for, including well outside its own bounds. Both passes share
// glsl-backdrop.js so they cannot disagree about what that coordinate contains.
//
// This is why the pane is attached to an element rather than drawn full-screen: the shader only
// has to cover the pane's own rectangle. A blur of the same visual weight is a full-screen read
// plus a separable two-pass convolution, every frame, which is precisely why scroll-guard.js
// exists to switch the CSS version off the moment a list starts moving.
//
// RULES THIS FOLLOWS, ALL OF THEM LOAD-BEARING.
//
//   * The DOM element stays exactly where it is. The canvas is a sibling behind its content,
//     aria-hidden and pointer-events:none. If the shader never starts — no WebGL2, reduced
//     motion, a downgraded tier — the CSS `.glass` underneath is what shows, which is what
//     shipped before this file existed and is a complete material on its own.
//   * `full` tier only, and it takes a pooled surface from gl-pool like everything else, so its
//     cost lands in the same 20ms budget that can downgrade it.
//   * The element's rect is re-read every frame. A pane whose geometry is captured once is wrong
//     after the first scroll, and wrong glass is worse than no glass: the refraction would point
//     at a part of the scene that is not behind it.

import { gfx, createSurface, compileProgram, FULLSCREEN_VERTEX_SHADER } from './gfx-core.js';
import { BACKDROP_UNIFORMS, BACKDROP_GLSL } from './glsl-backdrop.js';

const FRAGMENT_SHADER = `#version 300 es
precision mediump float;

in vec2 vUv;
out vec4 fragColor;
${BACKDROP_UNIFORMS}
${BACKDROP_GLSL}

// Where this pane sits in the backdrop's own aspect-corrected space: xy = origin, zw = size.
// Supplied per frame from the element's bounding rect, because a pane that captured its geometry
// once refracts the wrong part of the scene the moment the page scrolls.
uniform vec4  uPaneRect;
uniform float uRadius;     // corner radius, in pane-space units
uniform float uThickness;  // 0..1, how much the edges bend
uniform float uTintAmount; // 0..1, how far toward uTintColor the transmitted light is pulled
uniform vec3  uTintColor;
uniform float uSheen;      // 0..1, strength of the travelling highlight

/**
 * The pane's edge field: 0 at the centre, 1 at the boundary, smooth everywhere in between.
 *
 * NOT a rounded-rectangle SDF, and that is the whole point. The obvious 'min(max(q.x,q.y),0.0)'
 * box SDF is exact, but INSIDE the box its gradient is axis-aligned and flips which axis
 * dominates along the diagonals. Taking a normal from it therefore produces a hard seam from
 * each corner to the centre — a visible X across the pane, which is what the first version of
 * this shader shipped.
 *
 * A superellipse has no such switch: it is one smooth expression over the whole interior, and
 * its gradient is continuous, so the refraction sweeps around the corners instead of snapping.
 * The exponent is the corner roundness — 2 is an ellipse, high values approach a rectangle.
 */
float edgeField(vec2 p, vec2 halfSize, float power) {
    vec2 n = abs(p) / max(halfSize, vec2(1e-5));
    return pow(pow(n.x, power) + pow(n.y, power), 1.0 / power);
}

void main() {
    PoseeScene s = posee_scene();

    // vUv is 0..1 across the PANE. Map it into the backdrop's space so every lookup below is
    // directly comparable to what the full-screen pass would have drawn at that coordinate.
    vec2 paneUv = vUv;
    vec2 scenePos = uPaneRect.xy + paneUv * uPaneRect.zw;

    // Pane-local coordinates, centred, in scene units — so the profile is not distorted by a
    // wide or short pane the way it would be in raw 0..1 uv.
    vec2 local = (paneUv - 0.5) * uPaneRect.zw;
    vec2 halfSize = uPaneRect.zw * 0.5;

    // Corner roundness from the element's own radius. A large radius is a soft corner (low
    // exponent); a small one approaches a rectangle. Approximate by construction — the CANVAS is
    // clipped to the real radius by 'border-radius: inherit' in app.css, so this only has to get
    // the shape of the light right, never the silhouette.
    float power = mix(8.0, 2.5, clamp(uRadius / max(min(halfSize.x, halfSize.y), 1e-5), 0.0, 1.0));

    float f = edgeField(local, halfSize, power);

    // ── Thickness profile ───────────────────────────────────────────────────────────────
    //
    // A real slab is thickest in the middle and its SURFACE is steepest at the rim, which is why
    // a glass edge distorts hard and a glass centre barely distorts at all. 'edge' is 0 well
    // inside and rises to 1 at the boundary; everything below is driven by it.
    float edge = smoothstep(0.55, 1.0, f);
    edge = pow(clamp(edge, 0.0, 1.0), 0.8);

    // Outward normal of that field, by central difference. Smooth because the field is.
    float e = max(halfSize.x, halfSize.y) * 0.01;
    vec2 sdfNormal = normalize(vec2(
        edgeField(local + vec2(e, 0.0), halfSize, power) - edgeField(local - vec2(e, 0.0), halfSize, power),
        edgeField(local + vec2(0.0, e), halfSize, power) - edgeField(local - vec2(0.0, e), halfSize, power)
    ) + vec2(1e-6));

    // ── Refraction, with dispersion ─────────────────────────────────────────────────────
    //
    // Three lookups at three offsets rather than one at an average. Real dispersion is the whole
    // reason a glass edge fringes colour, and it is the single detail that separates this from a
    // displacement map: the eye reads the fringe as material thickness without being told.
    //
    // Sampling OUTWARD (along +normal) is correct for a converging slab — light reaching the eye
    // through the rim came from beyond the pane's footprint, not from under it.
    float bend = edge * uThickness * min(halfSize.x, halfSize.y) * 0.5;
    vec2 dir = sdfNormal;

    vec3 refracted;
    refracted.r = posee_backdrop(scenePos + dir * bend * 1.10, s).r;
    refracted.g = posee_backdrop(scenePos + dir * bend * 1.00, s).g;
    refracted.b = posee_backdrop(scenePos + dir * bend * 0.90, s).b;

    // ── Transmission tint ───────────────────────────────────────────────────────────────
    //
    // Glass is not colourless and neither is this design system. Pulling the transmitted light
    // toward the surface token keeps the pane recognisably part of the app rather than a hole
    // cut in it, and is also what keeps text on top readable — the CSS card underneath supplies
    // the actual contrast, and this must not fight it.
    vec3 color = mix(refracted, uTintColor, uTintAmount);

    // ── Specular ────────────────────────────────────────────────────────────────────────
    //
    // Two terms doing different jobs. The rim term is the bright line along the top-left edge
    // that says "this has a thickness"; the sheen is a slow band travelling across the face,
    // which is what makes the pane read as a surface catching a moving light rather than as a
    // static texture. The sheen is tied to the scene's own time so every pane on the page agrees
    // about where the light is.
    // Upper-left, in the shader's y-UP space — so this lights the top edge, where a viewer
    // expects the bright line on a pane lit from above. Negating the y here (which reads
    // "upper-left" in CSS coordinates) puts the highlight along the bottom instead, and a slab
    // lit from below looks like it is floating in the wrong direction.
    float rimLight = pow(edge, 2.4) * max(0.0, dot(sdfNormal, normalize(vec2(-0.6, 0.8))));
    color += vec3(1.0) * rimLight * 0.38;

    // A single wide band travelling across the face. 'paneUv.x * 1.4 + paneUv.y * 0.6' rather
    // than an equal sum: an exactly 45-degree band crosses a card's own corners simultaneously
    // and reads as a fold in the surface rather than as a reflection moving across it.
    float sweep = sin((paneUv.x * 1.4 + paneUv.y * 0.6) * 3.0 - uTime * 0.35);
    float sheen = pow(max(0.0, sweep), 10.0) * uSheen * (1.0 - edge * 0.5);
    color += vec3(1.0) * sheen * 0.10;

    // ── Coverage ────────────────────────────────────────────────────────────────────────
    //
    // Deliberately flat. The canvas carries 'border-radius: inherit' (see .fx-overlay in
    // app.css), so the BROWSER already clips this to the card's exact corner — computing a
    // second silhouette here would only be a chance for the two to disagree by a pixel.
    //
    // Alpha is below 1 on purpose. The CSS card underneath is what guarantees the contrast ratio
    // for the text on top; this is a material over it, not a replacement for it, and an opaque
    // pane would silently take a measured background out of that calculation.
    fragColor = vec4(color, 0.92);
}`;

const instances = new Map();
let nextId = 1;

/** Parses a CSS colour the cheap way — the browser already has a parser, so borrow it. */
function resolveTint(cssColor, fallback) {
    try {
        const probe = document.createElement('canvas').getContext('2d');
        if (!probe) return fallback;
        probe.fillStyle = '#000';
        probe.fillStyle = cssColor;
        const resolved = probe.fillStyle;
        const match = /^#?([0-9a-f]{6})$/i.exec(resolved);
        if (!match) return fallback;
        const int = parseInt(match[1], 16);
        return [((int >> 16) & 255) / 255, ((int >> 8) & 255) / 255, (int & 255) / 255];
    } catch {
        return fallback;
    }
}

/** The surface token, so a pane is the right colour in both themes without a literal here. */
function readTint() {
    try {
        const value = getComputedStyle(document.documentElement)
            .getPropertyValue('--color-card').trim();
        return resolveTint(value || '#ffffff', [1, 1, 1]);
    } catch {
        return [1, 1, 1];
    }
}

/**
 * Starts a pane.
 *
 * @param {HTMLCanvasElement} canvas overlay canvas sized to the element; aria-hidden, pointer-events:none
 * @param {HTMLElement} target the element the pane is standing in for — its rect drives the refraction
 * @param {{ thickness?: number, tint?: number, sheen?: number, radius?: number }} options
 * @returns {number} handle, or 0 if it did not start
 */
export function start(canvas, target, options = {}) {
    if (!canvas || !target || !gfx.allows('full')) {
        return 0;
    }

    const surface = createSurface(canvas, { maxDpr: 1.5 });
    if (!surface) {
        return 0;
    }

    const { gl } = surface;

    let program;
    try {
        program = compileProgram(gl, FULLSCREEN_VERTEX_SHADER, FRAGMENT_SHADER);
    } catch (err) {
        // The CSS `.glass` material underneath is a complete answer on its own.
        console.warn('[glass] shader unavailable; CSS glass stays', err);
        surface.release();
        return 0;
    }

    const uniforms = {
        time: gl.getUniformLocation(program, 'uTime'),
        resolution: gl.getUniformLocation(program, 'uResolution'),
        strange: gl.getUniformLocation(program, 'uStrange'),
        colorA: gl.getUniformLocation(program, 'uColorA'),
        colorB: gl.getUniformLocation(program, 'uColorB'),
        colorC: gl.getUniformLocation(program, 'uColorC'),
        keyLight: gl.getUniformLocation(program, 'uKeyLight'),
        audio: gl.getUniformLocation(program, 'uAudio'),
        audioBass: gl.getUniformLocation(program, 'uAudioBass'),
        paneRect: gl.getUniformLocation(program, 'uPaneRect'),
        radius: gl.getUniformLocation(program, 'uRadius'),
        thickness: gl.getUniformLocation(program, 'uThickness'),
        tintAmount: gl.getUniformLocation(program, 'uTintAmount'),
        tintColor: gl.getUniformLocation(program, 'uTintColor'),
        sheen: gl.getUniformLocation(program, 'uSheen'),
        ambient: gl.getUniformLocation(program, 'uAmbient')
    };

    const vao = gl.createVertexArray();
    const id = nextId++;
    // Seeded with the theme's own scene so the very first frame is already in register with the
    // backdrop. fx.js calls setScene immediately afterwards to pick up the live comic, but a pane
    // must never draw one frame of a default scene — at this size that frame is a visible flash.
    const scene = readSceneTokens();

    const instance = {
        gl,
        surface,
        program,
        vao,
        canvas,
        target,
        startTime: performance.now(),
        strange: 0.4,
        ambient: scene.ambient,
        colorA: scene.colors[0],
        colorB: scene.colors[1],
        colorC: scene.colors[2],
        thickness: Math.min(1, Math.max(0, options.thickness ?? 0.55)),
        tintAmount: Math.min(1, Math.max(0, options.tint ?? 0.62)),
        sheen: Math.min(1, Math.max(0, options.sheen ?? 0.7)),
        radiusPx: options.radius ?? null,
        tintColor: readTint(),
        audio: 0,
        audioBass: 0,
        stop: null
    };

    instance.stop = gfx.addTask(`glass#${id}`, (now) => {
        if (!surface.beginFrame()) {
            stop(id);
            return;
        }

        const rect = instance.target.getBoundingClientRect();

        // An element scrolled out of view, or collapsed by a re-render, still has a rect — it is
        // just empty or off-screen. Drawing it is a wasted pass, and on a zero-width rect the
        // SDF divides by zero and paints a solid block.
        if (rect.width < 1 || rect.height < 1) {
            surface.present();
            return;
        }

        const vw = Math.max(1, window.innerWidth);
        const vh = Math.max(1, window.innerHeight);
        const aspect = vw / vh;

        // Into the backdrop's aspect-corrected space: x scaled by aspect, y in 0..1. This is the
        // same mapping gradient.js's main() performs, which is the whole reason a lookup here
        // returns the pixel that is genuinely behind this pane.
        //
        // THE Y FLIP IS THE POINT. The fullscreen triangle sets vUv = gl_Position * 0.5 + 0.5, so
        // vUv.y = 1 is the TOP of the frame — while getBoundingClientRect().top is measured DOWN
        // from the top of the viewport. Mapping rect.top straight to scene y makes the pane
        // sample the scene mirrored about the horizontal axis: it still looks like a material,
        // which is why this survives a glance, but it is refracting the wrong part of the page.
        // (Same class of mistake as the present() flip documented in gl-pool.js.)
        const paneX = (rect.left / vw) * aspect;
        const paneY = 1 - (rect.bottom / vh);
        const paneW = (rect.width / vw) * aspect;
        const paneH = rect.height / vh;

        // Corner radius read from the element rather than configured, so the pane cannot drift
        // out of register with the card it is standing in for after a token change.
        let radiusPx = instance.radiusPx;
        if (radiusPx === null) {
            const parsed = parseFloat(getComputedStyle(instance.target).borderTopLeftRadius);
            radiusPx = Number.isFinite(parsed) ? parsed : 16;
        }
        // Clamped to half the shorter side: a radius larger than that is not a rounded rectangle
        // any more and the SDF inverts.
        const radius = Math.min(
            (radiusPx / vh),
            Math.min(paneW, paneH) * 0.5);

        gl.useProgram(program);
        gl.bindVertexArray(vao);
        gl.uniform1f(uniforms.time, (now - instance.startTime) / 1000);
        gl.uniform2f(uniforms.resolution, vw, vh);
        gl.uniform1f(uniforms.strange, instance.strange);
        gl.uniform3fv(uniforms.colorA, instance.colorA);
        gl.uniform3fv(uniforms.colorB, instance.colorB);
        gl.uniform3fv(uniforms.colorC, instance.colorC);
        gl.uniform3fv(uniforms.keyLight, [1.00, 0.86, 0.62]);
        gl.uniform1f(uniforms.audio, instance.audio);
        gl.uniform1f(uniforms.audioBass, instance.audioBass);
        gl.uniform4f(uniforms.paneRect, paneX, paneY, paneW, paneH);
        gl.uniform1f(uniforms.radius, radius);
        gl.uniform1f(uniforms.thickness, instance.thickness);
        gl.uniform1f(uniforms.tintAmount, instance.tintAmount);
        gl.uniform3fv(uniforms.tintColor, instance.tintColor);
        gl.uniform1f(uniforms.sheen, instance.sheen);
        gl.uniform1f(uniforms.ambient, instance.ambient);
        gl.drawArrays(gl.TRIANGLES, 0, 3);
        gl.bindVertexArray(null);

        surface.present();
    });

    instances.set(id, instance);
    // Tells app.css to stand the CSS material down for this element. Written only once the
    // shader has actually started, so a pane that never ran keeps its blur.
    try {
        target.dataset.glassPane = 'on';
    } catch { /* detached element */ }
    return id;
}

/**
 * Retunes every live pane to the current scene.
 *
 * The colour maths is deliberately IDENTICAL to gradient.setPalette's, for the same reason the
 * GLSL is shared: a pane whose base colours differ from the backdrop's by even a little stops
 * refracting and starts looking like a differently-tinted rectangle. fx.js is the only caller,
 * and it calls both sides from the same place.
 */
export function setScene(score, palette) {
    const strange = Math.min(1, Math.max(0, (score ?? 40) / 100));
    const colors = Array.isArray(palette) && palette.length >= 3
        ? palette.map(parse)
        : null;
    const scene = readSceneTokens();

    for (const instance of instances.values()) {
        instance.strange = strange;
        instance.ambient = scene.ambient;
        instance.colorA = colors ? blend(scene.colors[0], colors[0], 0.18) : scene.colors[0];
        instance.colorB = colors ? blend(scene.colors[1], colors[1], 0.42) : scene.colors[1];
        instance.colorC = colors ? blend(scene.colors[2], colors[2], 0.75) : scene.colors[2];
    }
}

function parse(hex) {
    const match = /^#?([0-9a-f]{6})$/i.exec(String(hex ?? '').trim());
    if (!match) return [0.5, 0.5, 0.5];
    const int = parseInt(match[1], 16);
    return [((int >> 16) & 255) / 255, ((int >> 8) & 255) / 255, (int & 255) / 255];
}

function blend(base, comic, amount) {
    return [
        base[0] + (comic[0] - base[0]) * amount,
        base[1] + (comic[1] - base[1]) * amount,
        base[2] + (comic[2] - base[2]) * amount
    ];
}

/** The theme's scene base, matching gradient.js's readSceneTokens. */
function readSceneTokens() {
    const dark = (() => {
        try {
            const explicit = document.documentElement.dataset.theme;
            if (explicit === 'dark') return true;
            if (explicit === 'light') return false;
            return window.matchMedia?.('(prefers-color-scheme: dark)').matches ?? false;
        } catch {
            return false;
        }
    })();

    const fallback = dark
        ? [[0.10, 0.03, 0.20], [0.16, 0.09, 0.30], [0.58, 0.36, 0.96]]
        : [[0.976, 0.973, 1.0], [0.929, 0.914, 1.0], [0.486, 0.227, 0.929]];

    try {
        const style = getComputedStyle(document.documentElement);
        const pick = (name, or) => parse(style.getPropertyValue(name).trim()) ?? or;
        return {
            ambient: dark ? 0.62 : 0.94,
            colors: [
                pick('--color-surface', fallback[0]),
                pick('--color-brand-surface', fallback[1]),
                pick('--color-brand', fallback[2])
            ]
        };
    } catch {
        return { ambient: dark ? 0.62 : 0.94, colors: fallback };
    }
}

/** Feeds the analyser in, exactly as the gradient does, so panes breathe with the backdrop. */
export function setAudioLevels(level, bass) {
    for (const instance of instances.values()) {
        instance.audio += (level - instance.audio) * 0.25;
        instance.audioBass += (bass - instance.audioBass) * 0.25;
    }
}

export function stop(id) {
    const instance = instances.get(id);
    if (!instance) return;

    instance.stop?.();
    instance.gl.deleteProgram(instance.program);
    instance.gl.deleteVertexArray(instance.vao);
    instance.surface.release();
    try {
        // The CSS material comes back the moment this is gone. Removing the flag has to be
        // unconditional: a pane torn down with the attribute still set leaves the element with
        // no glass at all, which is the one outcome worse than either material alone.
        delete instance.target.dataset.glassPane;
    } catch { /* detached element */ }
    instances.delete(id);
}

/** Live pane count, for the diagnostics panel. */
export function activeCount() {
    return instances.size;
}

/**
 * Tears every pane down at once. Called by fx.js when the backdrop these refract stops running —
 * a pane without its scene is not a degraded pane, it is a differently-coloured rectangle in the
 * middle of the page.
 */
export function stopAll() {
    for (const id of [...instances.keys()]) stop(id);
}

gfx.onTierChanged((tier) => {
    if (tier !== 'full') {
        for (const id of [...instances.keys()]) stop(id);
    }
});
