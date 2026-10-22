// Ink blooming in water behind the comic-generation wait.
//
// THE MOMENT THIS IS FOR. A generation is about ten seconds of five streamed phases ("Reading the
// reviews…", "Scoring the strangeness…", …) over a storyboard of empty frames. That wait is the
// most-watched ten seconds in the app and it was a progress label. Each phase now squirts its own
// colour of ink in from a different edge; "Drawing the panels" churns hardest; and when the strip
// arrives the ink is pulled down the drain into the centre, so the comic's own reveal lands on a
// clean page. The pointer stirs the water, so a bored user has something to play with.
//
// THE MODEL. Jos Stam's stable fluids, in the GPU Gems ch.38 arrangement: a velocity field and a
// dye field, semi-Lagrangian advection, vorticity confinement (what keeps small curls alive at a
// coarse grid), divergence, a Jacobi pressure solve, gradient subtraction. Dye is stored as
// premultiplied colour + density (rgb = colour·density, a = density), so two inks that meet MIX
// rather than one overwriting the other, and the display pass can recover the hue at any density.
//
// COST DECISIONS, in order of how much they matter:
//   * Velocity/pressure run at 128 texels on the short side — every one of the ~30 sim passes per
//     frame is a few thousand fragments. Only the dye (512 short side) and the one display pass
//     (canvas pixels, DPR capped at 1.25) touch real fill rate.
//   * Half-float targets only (RGBA16F/RG16F/R16F). 16F linear filtering is core in WebGL2, so
//     advection gets hardware bilinear for free; no 32F, no OES_texture_float_linear needed.
//     With neither EXT_color_buffer_float nor EXT_color_buffer_half_float there is no renderable
//     float target and start() returns 0 — the page simply has no ink.
//   * The drain is NOT a force on the velocity field: a radial sink is pure divergence and the
//     pressure projection erases it every frame, and a hard pull through semi-Lagrangian
//     advection smears into radial streaks. It is a twisting, contracting warp of the display
//     lookup instead — a whirlpool — with the dye's dissipation turned up underneath it.
//   * 'full' tier only, on the shared scheduler. A drop below 'full', a lost context or a canvas
//     Blazor has detached all tear down to a cleared, transparent canvas.
//
// LOOK. Density maps to alpha through 1 − e^(−kd) so thin washes stay translucent; dense ink goes
// deeper and more saturated (subtractive, like real pigment pooling); the density gradient darkens
// the wet edge (watercolour edge-darkening) and gives a faint wet sheen; paper tooth and a dither
// keep it off the 8-bit banding ladder. Light theme composites normally — additive glow vanishes
// on a pale page — and dark theme adds a soft bloom from an 8-tap ring around each pixel.

import { gfx, createSurface, compileProgram, FULLSCREEN_VERTEX_SHADER } from './gfx-core.js';

const SIM_RES = 128;
const DYE_RES = 512;
const DYE_MAX = 1024;
const PRESSURE_ITERATIONS = 20;
const PRESSURE_DECAY = 0.8;
const DRAIN_MS = 700;
const ENERGY_INTERVAL_MS = 125;            // onEnergy at most ~8×/s
const VORTICITY_CALM = 16;
const VORTICITY_CHURN = 42;
const VELOCITY_DISSIPATION = 0.35;
const DYE_DISSIPATION = 0.28;
const STIR_FORCE = 480;
const POINTER_MAX = 600;                   // sim texels/s; a flick stirs rather than detonates                    // sim texels/s added per second at a spoon

// Cycled by phase index. Danger red was the obvious fifth, but next to the brand-alt coral it
// reads as the same ink twice and as an error on the last step; the orchid closes on the brand.
const PHASE_TOKENS = ['--color-brand', '--color-accent', '--color-brand-alt', '--color-success', '--color-brand-alt-soft'];
const PHASE_FALLBACK = ['#7C3AED', '#F59E0B', '#f5576c', '#10b981', '#f093fb'];

// Each phase arrives from a different side: successive golden-angle steps never repeat a side
// within five phases, and adjacent phases land far apart.
const GOLDEN_ANGLE = 2.39996;

// ── Shaders ──────────────────────────────────────────────────────────────────────────────

const SIM_HEADER = `#version 300 es
precision highp float;
precision highp sampler2D;
in vec2 vUv;
out vec4 fragColor;
`;

const ADVECT_FS = `${SIM_HEADER}
uniform sampler2D uVelocity;
uniform sampler2D uSource;
uniform vec2 uSimTexel;
uniform float uDt;
uniform float uDissipation;
uniform float uEdgeFade;  // extra dissipation at the walls, so ink does not pile up against them
void main() {
    vec2 coord = vUv - uDt * texture(uVelocity, vUv).xy * uSimTexel;
    vec2 wall = min(vUv, 1.0 - vUv);
    float edge = 1.0 - smoothstep(0.0, 0.14, min(wall.x, wall.y));
    fragColor = texture(uSource, coord) / (1.0 + (uDissipation + uEdgeFade * edge) * uDt);
}`;

const SPLAT_FS = `${SIM_HEADER}
uniform sampler2D uTarget;
uniform float uAspect;
uniform vec2 uPoint;
uniform vec2 uDir;       // unit, aspect-corrected; the splat is stretched along it
uniform float uRadius;
uniform float uStretch;  // < 1 elongates along uDir — a jet, not a blob
uniform vec4 uValue;
void main() {
    vec2 p = vUv - uPoint;
    p.x *= uAspect;
    float along = dot(p, uDir);
    float across = dot(p, vec2(-uDir.y, uDir.x));
    float w = exp(-(along * along * uStretch + across * across) / uRadius);
    fragColor = texture(uTarget, vUv) + w * uValue;
}`;

// Sustained swirl for the "Drawing the panels" churn: a rotation is divergence-free, so unlike the
// drain it survives the pressure projection and genuinely spins the water.
const SWIRL_FS = `${SIM_HEADER}
uniform sampler2D uVelocity;
uniform float uAspect;
uniform float uDt;
uniform float uSwirl;
uniform float uTime;
void main() {
    // Three eddies orbiting the centre, alternating in spin. One big ring would be a centrifuge
    // that flings the ink onto the walls; opposed eddies fold it over itself and keep it mid-frame.
    vec2 v = texture(uVelocity, vUv).xy;
    for (int i = 0; i < 3; i++) {
        float a = uTime * 0.55 + float(i) * 2.0944;
        vec2 c = vec2(0.5) + vec2(cos(a) * 0.20 / uAspect, sin(a) * 0.17);
        vec2 p = (vUv - c) * vec2(uAspect, 1.0);
        float r2 = dot(p, p);
        float spin = (i == 1) ? -1.0 : 1.0;
        v += uDt * uSwirl * spin * exp(-r2 / 0.012) * vec2(-p.y, p.x) / (sqrt(r2) + 0.03);
    }
    fragColor = vec4(v, 0.0, 1.0);
}`;

const CURL_FS = `${SIM_HEADER}
uniform sampler2D uVelocity;
uniform vec2 uTexel;
void main() {
    float L = texture(uVelocity, vUv - vec2(uTexel.x, 0.0)).y;
    float R = texture(uVelocity, vUv + vec2(uTexel.x, 0.0)).y;
    float T = texture(uVelocity, vUv + vec2(0.0, uTexel.y)).x;
    float B = texture(uVelocity, vUv - vec2(0.0, uTexel.y)).x;
    fragColor = vec4(0.5 * (R - L - T + B), 0.0, 0.0, 1.0);
}`;

const VORTICITY_FS = `${SIM_HEADER}
uniform sampler2D uVelocity;
uniform sampler2D uCurl;
uniform vec2 uTexel;
uniform float uCurlStrength;
uniform float uDt;
void main() {
    float L = texture(uCurl, vUv - vec2(uTexel.x, 0.0)).x;
    float R = texture(uCurl, vUv + vec2(uTexel.x, 0.0)).x;
    float T = texture(uCurl, vUv + vec2(0.0, uTexel.y)).x;
    float B = texture(uCurl, vUv - vec2(0.0, uTexel.y)).x;
    float C = texture(uCurl, vUv).x;
    vec2 force = 0.5 * vec2(abs(T) - abs(B), abs(R) - abs(L));
    force /= length(force) + 0.0001;
    force *= uCurlStrength * C;
    force.y *= -1.0;
    vec2 v = texture(uVelocity, vUv).xy + force * uDt;
    fragColor = vec4(clamp(v, vec2(-1200.0), vec2(1200.0)), 0.0, 1.0);
}`;

// Reflective walls: a neighbour outside the grid mirrors the centre's normal component, so ink
// slides along the frame edge instead of draining out of it.
const DIVERGENCE_FS = `${SIM_HEADER}
uniform sampler2D uVelocity;
uniform vec2 uTexel;
void main() {
    vec2 C = texture(uVelocity, vUv).xy;
    float L = texture(uVelocity, vUv - vec2(uTexel.x, 0.0)).x;
    float R = texture(uVelocity, vUv + vec2(uTexel.x, 0.0)).x;
    float T = texture(uVelocity, vUv + vec2(0.0, uTexel.y)).y;
    float B = texture(uVelocity, vUv - vec2(0.0, uTexel.y)).y;
    if (vUv.x - uTexel.x < 0.0) L = -C.x;
    if (vUv.x + uTexel.x > 1.0) R = -C.x;
    if (vUv.y + uTexel.y > 1.0) T = -C.y;
    if (vUv.y - uTexel.y < 0.0) B = -C.y;
    fragColor = vec4(0.5 * (R - L + T - B), 0.0, 0.0, 1.0);
}`;

const SCALE_FS = `${SIM_HEADER}
uniform sampler2D uTexture;
uniform float uValue;
void main() {
    fragColor = uValue * texture(uTexture, vUv);
}`;

const PRESSURE_FS = `${SIM_HEADER}
uniform sampler2D uPressure;
uniform sampler2D uDivergence;
uniform vec2 uTexel;
void main() {
    float L = texture(uPressure, vUv - vec2(uTexel.x, 0.0)).x;
    float R = texture(uPressure, vUv + vec2(uTexel.x, 0.0)).x;
    float T = texture(uPressure, vUv + vec2(0.0, uTexel.y)).x;
    float B = texture(uPressure, vUv - vec2(0.0, uTexel.y)).x;
    float div = texture(uDivergence, vUv).x;
    fragColor = vec4((L + R + B + T - div) * 0.25, 0.0, 0.0, 1.0);
}`;

const GRADIENT_FS = `${SIM_HEADER}
uniform sampler2D uPressure;
uniform sampler2D uVelocity;
uniform vec2 uTexel;
void main() {
    float L = texture(uPressure, vUv - vec2(uTexel.x, 0.0)).x;
    float R = texture(uPressure, vUv + vec2(uTexel.x, 0.0)).x;
    float T = texture(uPressure, vUv + vec2(0.0, uTexel.y)).x;
    float B = texture(uPressure, vUv - vec2(0.0, uTexel.y)).x;
    vec2 v = texture(uVelocity, vUv).xy - vec2(R - L, T - B);
    fragColor = vec4(v, 0.0, 1.0);
}`;

// Output is PREMULTIPLIED (the surface is created premultipliedAlpha: true). In dark mode the
// bloom is added to rgb without adding alpha — that is what additive light means premultiplied.
const DISPLAY_FS = `#version 300 es
precision highp float;
precision mediump sampler2D;
in vec2 vUv;
out vec4 fragColor;
uniform sampler2D uDye;
uniform vec2 uDyeTexel;
uniform float uDark;
uniform float uFade;
uniform float uAspect;
uniform float uDrain;   // 0..1

// The drain is a whirlpool in the LOOKUP, not in the fluid: the page samples the dye from an
// ever-larger, twisting neighbourhood, so the ink visibly contracts and spirals into the centre.
vec2 drainWarp(vec2 uv) {
    if (uDrain <= 0.0) return uv;
    vec2 p = (uv - 0.5) * vec2(uAspect, 1.0);
    float r = length(p);
    float t = uDrain * uDrain;
    float twist = t * 4.5 / (1.0 + r * 5.0);
    float s = sin(twist), c = cos(twist);
    p = mat2(c, s, -s, c) * p * (1.0 + 4.0 * t);
    return p / vec2(uAspect, 1.0) + 0.5;
}

float hash(vec2 p) {
    p = fract(p * vec2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return fract(p.x * p.y);
}

float vnoise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    vec2 u = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), u.x),
               mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), u.x), u.y);
}

void main() {
    vec2 uv = drainWarp(vUv);
    vec4 c = texture(uDye, uv);
    float d = max(c.a, 0.0);
    vec3 col = clamp(c.rgb / max(d, 0.002), 0.0, 1.0);

    // Density gradient at ~2 dye texels: the wet front where pigment piles up as it dries.
    vec2 e = uDyeTexel * 2.0;
    float dl = texture(uDye, uv - vec2(e.x, 0.0)).a;
    float dr = texture(uDye, uv + vec2(e.x, 0.0)).a;
    float du = texture(uDye, uv + vec2(0.0, e.y)).a;
    float dd = texture(uDye, uv - vec2(0.0, e.y)).a;
    vec2 g = vec2(dr - dl, du - dd);
    float slope = length(g);
    float edge = smoothstep(0.02, 0.22, slope) * smoothstep(0.03, 0.25, d) * (1.0 - smoothstep(1.2, 3.0, d));

    // A small floor keeps the faint haze that semi-Lagrangian smearing leaves behind off the page:
    // without it, ten seconds of ink tints the whole box and the shapes lose their edges.
    float body = 1.0 - exp(-max(d - mix(0.035, 0.09, uDark), 0.0) * 2.0);

    // Paper tooth: ink settles into the grain on a light page; barely there on a dark one.
    vec2 px = gl_FragCoord.xy;
    float tooth = vnoise(px * 0.55) * 0.55 + vnoise(px * 0.13 + 17.0) * 0.45;
    float fibre = vnoise(vec2(px.x * 0.035 + px.y * 0.01, px.y * 0.6));

    // Wet sheen: a faint highlight off the density slope, lit from the upper left.
    vec3 n = normalize(vec3(-g * 7.0, 1.0));
    float sheen = pow(max(dot(n, normalize(vec3(-0.5, 0.6, 0.62))), 0.0), 28.0) * smoothstep(0.1, 0.6, d);

    // Soft frame: the canvas rectangle must never read as a box behind the content.
    vec2 q = abs(uv - 0.5) * 2.0;
    float corner = pow(pow(q.x, 5.0) + pow(q.y, 5.0), 0.2);
    float mask = 1.0 - smoothstep(0.72, 0.99, corner);

    vec3 rgb;
    float alpha;
    if (uDark < 0.5) {
        // Pigment on paper (subtractive): thick ink goes deeper and more saturated, and the wet
        // edge pools darker in the SAME hue — darkening toward grey reads as dirt, not ink.
        vec3 deep = col * col * 0.9;
        vec3 ink = mix(col, deep, smoothstep(0.35, 2.4, d) * 0.7);
        ink = mix(ink, deep * 0.82, edge * 0.6);
        alpha = min(1.0, body + edge * 0.18);
        alpha *= 1.0 - 0.30 * (1.0 - tooth) - 0.06 * fibre;
        alpha *= mask * uFade * 0.86;
        rgb = ink * alpha + vec3(sheen) * alpha * 0.22;
    } else {
        // Luminous ink on a dark page: never darker than the ground. Thick cores run hot toward
        // white, the wet edge catches a rim of light, and rgb above alpha is the additive part.
        vec3 hot = mix(col, vec3(1.0), smoothstep(1.0, 3.5, d) * 0.35);
        hot = mix(hot, min(col * 1.35 + 0.12, vec3(1.0)), edge * 0.6);
        float lum = body * (1.0 - 0.10 * (1.0 - tooth)) * mask * uFade;
        alpha = lum * 0.62;
        rgb = hot * lum * 0.92 + vec3(sheen) * lum * 0.30;

        // Bloom: an 8-tap ring ~14 dye texels out approximates a wide blur for the price of 8 reads.
        vec4 glow = vec4(0.0);
        vec2 r = uDyeTexel * 14.0;
        for (int i = 0; i < 8; i++) {
            float a = float(i) * 0.785398 + 0.39;
            glow += texture(uDye, uv + vec2(cos(a), sin(a)) * r);
        }
        glow *= 0.125;
        vec3 glowCol = clamp(glow.rgb / max(glow.a, 0.002), 0.0, 1.0) * (1.0 - exp(-max(glow.a - 0.06, 0.0) * 1.2));
        rgb += glowCol * 0.28 * mask * uFade;
    }

    float dither = (hash(gl_FragCoord.xy + fract(uFade * 7.0)) - 0.5) / 255.0;
    fragColor = vec4(max(rgb + dither, 0.0), clamp(alpha + dither, 0.0, 1.0));
}`;

// ── Module state ─────────────────────────────────────────────────────────────────────────

const instances = new Map();
let nextId = 1;

const clamp01 = (v) => Math.min(1, Math.max(0, v));

/** Before the first frame has sized the targets, the canvas box is the best guess. */
const aspectOf = (inst) => inst.aspect
    || (inst.canvas.clientWidth / Math.max(1, inst.canvas.clientHeight))
    || 1;

function parseColor(hex, fallback) {
    const match = /^#?([0-9a-f]{6})$/i.exec((hex ?? '').trim());
    const source = match ? match[1] : fallback.slice(1);
    const int = parseInt(source, 16);
    return [((int >> 16) & 255) / 255, ((int >> 8) & 255) / 255, (int & 255) / 255];
}

function prefersDark() {
    try {
        const explicit = document.documentElement.dataset.theme;
        if (explicit === 'dark') return true;
        if (explicit === 'light') return false;
        return window.matchMedia?.('(prefers-color-scheme: dark)').matches ?? false;
    } catch {
        return false;
    }
}

function readPalette() {
    try {
        const style = getComputedStyle(document.documentElement);
        return PHASE_TOKENS.map((name, i) => parseColor(style.getPropertyValue(name), PHASE_FALLBACK[i]));
    } catch {
        return PHASE_FALLBACK.map((hex) => parseColor(hex, hex));
    }
}

// ── GL plumbing ──────────────────────────────────────────────────────────────────────────

function renderable(gl, internal, format) {
    const tex = gl.createTexture();
    gl.bindTexture(gl.TEXTURE_2D, tex);
    gl.texImage2D(gl.TEXTURE_2D, 0, internal, 4, 4, 0, format, gl.HALF_FLOAT, null);
    const fbo = gl.createFramebuffer();
    gl.bindFramebuffer(gl.FRAMEBUFFER, fbo);
    gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D, tex, 0);
    const ok = gl.checkFramebufferStatus(gl.FRAMEBUFFER) === gl.FRAMEBUFFER_COMPLETE;
    gl.bindFramebuffer(gl.FRAMEBUFFER, null);
    gl.deleteFramebuffer(fbo);
    gl.deleteTexture(tex);
    return ok;
}

/** RGBA16F is the floor; RG16F/R16F are used when renderable and fall back to RGBA16F. */
function pickFormats(gl) {
    const hasFloat = !!gl.getExtension('EXT_color_buffer_float');
    const hasHalf = hasFloat || !!gl.getExtension('EXT_color_buffer_half_float');
    if (!hasHalf) return null;
    const rgba = { internal: gl.RGBA16F, format: gl.RGBA };
    if (!renderable(gl, rgba.internal, rgba.format)) return null;
    const rg = renderable(gl, gl.RG16F, gl.RG) ? { internal: gl.RG16F, format: gl.RG } : rgba;
    const r = renderable(gl, gl.R16F, gl.RED) ? { internal: gl.R16F, format: gl.RED } : rg;
    return { rgba, rg, r };
}

function createTarget(gl, w, h, fmt) {
    const tex = gl.createTexture();
    gl.activeTexture(gl.TEXTURE0);
    gl.bindTexture(gl.TEXTURE_2D, tex);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
    gl.texImage2D(gl.TEXTURE_2D, 0, fmt.internal, w, h, 0, fmt.format, gl.HALF_FLOAT, null);
    const fbo = gl.createFramebuffer();
    gl.bindFramebuffer(gl.FRAMEBUFFER, fbo);
    gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D, tex, 0);
    gl.viewport(0, 0, w, h);
    gl.clearColor(0, 0, 0, 0);
    gl.clear(gl.COLOR_BUFFER_BIT);
    return { tex, fbo, w, h, texel: [1 / w, 1 / h] };
}

function createDouble(gl, w, h, fmt) {
    let a = createTarget(gl, w, h, fmt);
    let b = createTarget(gl, w, h, fmt);
    return {
        get read() { return a; },
        get write() { return b; },
        swap() { [a, b] = [b, a]; },
        targets: () => [a, b]
    };
}

function resolution(base, aspect, cap = Infinity) {
    const long = Math.min(cap, Math.round(base * (aspect < 1 ? 1 / aspect : aspect)));
    return aspect >= 1 ? [long, base] : [base, long];
}

function buildPrograms(gl) {
    const make = (fs, names) => {
        const program = compileProgram(gl, FULLSCREEN_VERTEX_SHADER, fs);
        const u = Object.fromEntries(names.map((n) => [n, gl.getUniformLocation(program, n)]));
        return { program, u };
    };
    return {
        advect: make(ADVECT_FS, ['uVelocity', 'uSource', 'uSimTexel', 'uDt', 'uDissipation', 'uEdgeFade']),
        splat: make(SPLAT_FS, ['uTarget', 'uAspect', 'uPoint', 'uDir', 'uRadius', 'uStretch', 'uValue']),
        swirl: make(SWIRL_FS, ['uVelocity', 'uAspect', 'uDt', 'uSwirl', 'uTime']),
        curl: make(CURL_FS, ['uVelocity', 'uTexel']),
        vorticity: make(VORTICITY_FS, ['uVelocity', 'uCurl', 'uTexel', 'uCurlStrength', 'uDt']),
        divergence: make(DIVERGENCE_FS, ['uVelocity', 'uTexel']),
        scale: make(SCALE_FS, ['uTexture', 'uValue']),
        pressure: make(PRESSURE_FS, ['uPressure', 'uDivergence', 'uTexel']),
        gradient: make(GRADIENT_FS, ['uPressure', 'uVelocity', 'uTexel']),
        display: make(DISPLAY_FS, ['uDye', 'uDyeTexel', 'uDark', 'uFade', 'uAspect', 'uDrain'])
    };
}

// ── Public API ───────────────────────────────────────────────────────────────────────────

/**
 * @param {HTMLCanvasElement} canvas sized by CSS; transparent wherever there is no ink
 * @param {{ onEnergy?: (e: number) => void, onDrained?: () => void }} options
 * @returns {number} handle, or 0 when nothing is running (below 'full', no float targets, …)
 */
export function start(canvas, options = {}) {
    try {
        if (!canvas || !gfx.allows('full')) return 0;
        for (const [id, existing] of instances) {
            if (existing.canvas === canvas) teardown(id, false);
        }

        const surface = createSurface(canvas, { maxDpr: 1.25 });
        if (!surface || surface.gl.isContextLost()) return 0;
        const { gl } = surface;

        const formats = pickFormats(gl);
        if (!formats) return 0;

        let programs;
        try {
            programs = buildPrograms(gl);
        } catch (err) {
            console.warn('[ink-fluid] shaders unavailable', err);
            return 0;
        }

        const id = nextId++;
        const inst = {
            id, canvas, surface, gl, formats, programs,
            vao: gl.createVertexArray(),
            targets: null,
            aspect: 0,
            onEnergy: typeof options.onEnergy === 'function' ? options.onEnergy : null,
            onDrained: typeof options.onDrained === 'function' ? options.onDrained : null,
            dark: prefersDark(),
            palette: readPalette(),
            emitters: [],
            churn: 0,
            churnTarget: 0,
            churnPulseAt: 0,
            curl: VORTICITY_CALM,
            time: 0,
            lastPhaseColor: null,
            pointer: { x: 0, y: 0, moved: false, prevU: null },
            energyPending: 0,
            energyAt: 0,
            draining: false,
            drainAt: 0,
            task: null,
            onPointer: null,
            onLost: null
        };

        inst.onPointer = (event) => {
            inst.pointer.x = event.clientX;
            inst.pointer.y = event.clientY;
            inst.pointer.moved = true;
        };
        inst.onLost = (event) => {
            event.preventDefault();
            teardown(id, true);
        };
        window.addEventListener('pointermove', inst.onPointer, { passive: true });
        canvas.addEventListener('webglcontextlost', inst.onLost);

        instances.set(id, inst);
        inst.task = gfx.addTask('ink-fluid', (now, elapsed) => frame(inst, now, elapsed));
        return id;
    } catch (err) {
        console.warn('[ink-fluid] start failed', err);
        return 0;
    }
}

/** A pipeline phase arrived: 2–4 jets of that phase's ink from a fresh edge. */
export function phase(handle, index, total) {
    try {
        const inst = instances.get(handle);
        if (!inst || inst.draining) return;
        const i = Math.max(0, Math.floor(Number(index) || 0));
        inst.dark = prefersDark();
        inst.palette = readPalette();

        const color = inst.palette[i % inst.palette.length];
        const previous = inst.lastPhaseColor ?? color;
        inst.lastPhaseColor = color;

        // "Drawing the panels" is the long, busy step: crank the curl and churn the water.
        inst.churnTarget = i === 2 ? 1 : 0;
        inst.churnPulseAt = performance.now() + 900;

        const isLast = Number(total) > 0 && i === Number(total) - 1;
        jets(inst, Math.PI + i * GOLDEN_ANGLE, 2 + (i % 3), color, previous, isLast ? 1.2 : 1);
    } catch {
        // A missed phase is a missed flourish, never an error the page sees.
    }
}

/**
 * Queues `count` jets fanned around `angle` (the side they come FROM, radians, 0 = right, y up).
 * `power` scales speed and ink: 1 for a phase, less for the churn's follow-up pulses.
 */
function jets(inst, angle, count, color, previous, power) {
    const now = performance.now();
    for (let k = 0; k < count; k++) {
        const from = angle + (k - (count - 1) / 2) * 0.42 + (Math.random() - 0.5) * 0.18;
        // Aim near — not at — the centre, so the jets shear past each other and roll up.
        const aimX = 0.5 + (Math.random() - 0.5) * 0.22;
        const aimY = 0.5 + (Math.random() - 0.5) * 0.22;
        const ox = 0.5 + Math.cos(from) * 0.40;
        const oy = 0.5 + Math.sin(from) * 0.38;
        const dx = (aimX - ox) * aspectOf(inst);
        const dy = aimY - oy;
        const len = Math.hypot(dx, dy) || 1;
        // A touch of the previous phase's ink in each jet keeps the water one body of colour.
        const mixAmount = 0.12 + Math.random() * 0.12;
        inst.emitters.push({
            at: now + k * 110 + Math.random() * 70,
            duration: 340 + Math.random() * 160,
            x: ox, y: oy,
            dir: [dx / len, dy / len],
            speed: (400 + Math.random() * 140) * power,
            color: color.map((c, ch) => c * (1 - mixAmount) + previous[ch] * mixAmount),
            amount: (3.4 + Math.random() * 1.2) * power,
            strength: Math.min(1, (0.55 + Math.random() * 0.35) * power),
            announced: false
        });
    }
}

/** The comic arrived: pull the ink down the drain over ~700ms, then stop and call onDrained. */
export function drain(handle) {
    try {
        const inst = instances.get(handle);
        if (!inst || inst.draining) return;
        inst.draining = true;
        inst.drainAt = performance.now();
        inst.emitters.length = 0;
        inst.churnTarget = 0;
    } catch {
        // stop() remains available to the caller.
    }
}

/** Immediate teardown. Idempotent; leaves a cleared, transparent canvas. Does not fire onDrained. */
export function stop(handle) {
    try {
        teardown(handle, false);
    } catch {
        // Already gone.
    }
}

// ── Per-frame ────────────────────────────────────────────────────────────────────────────

function ensureTargets(inst) {
    const { gl, surface, formats } = inst;
    const aspect = surface.width / Math.max(1, surface.height);
    if (inst.targets && Math.abs(aspect / inst.aspect - 1) < 0.15) return;

    // First frame, or the box changed shape enough (phone rotation) that stretched ink would show.
    destroyTargets(inst);
    inst.aspect = aspect;
    const [sw, sh] = resolution(SIM_RES, aspect, SIM_RES * 3);
    const [dw, dh] = resolution(DYE_RES, aspect, DYE_MAX);
    inst.targets = {
        velocity: createDouble(gl, sw, sh, formats.rg),
        pressure: createDouble(gl, sw, sh, formats.r),
        divergence: createTarget(gl, sw, sh, formats.r),
        curl: createTarget(gl, sw, sh, formats.r),
        dye: createDouble(gl, dw, dh, formats.rgba)
    };
}

function destroyTargets(inst) {
    const t = inst.targets;
    if (!t) return;
    const { gl } = inst;
    for (const target of [...t.velocity.targets(), ...t.pressure.targets(), ...t.dye.targets(), t.divergence, t.curl]) {
        gl.deleteFramebuffer(target.fbo);
        gl.deleteTexture(target.tex);
    }
    inst.targets = null;
}

function frame(inst, now, elapsed) {
    if (!instances.has(inst.id)) return;
    const { gl, surface, canvas } = inst;
    if (!canvas.isConnected || gl.isContextLost()) {
        teardown(inst.id, true);
        return;
    }
    if (!surface.beginFrame()) return;

    ensureTargets(inst);
    const dt = elapsed > 0 ? Math.min(elapsed / 1000, 1 / 30) : 1 / 60;
    inst.time += dt;
    gl.bindVertexArray(inst.vao);

    let drainT = 0;
    if (inst.draining) {
        drainT = clamp01((now - inst.drainAt) / DRAIN_MS);
    }

    // Builds over ~1s, so the phase's jets reach the middle before the water starts to turn.
    inst.churn += (inst.churnTarget - inst.churn) * Math.min(1, dt * (inst.churnTarget > inst.churn ? 1.6 : 1.0));
    inst.curl = VORTICITY_CALM + (VORTICITY_CHURN - VORTICITY_CALM) * inst.churn;

    if (!inst.draining) {
        stir(inst, dt);
        runEmitters(inst, now, dt);
        stirFromPointer(inst, dt, now);
        if (inst.churn > 0.01) swirl(inst, dt);
        // While it churns, the step keeps feeding itself: a smaller jet from a random side
        // every ~0.7s, so a long "Drawing the panels" never settles back into calm.
        if (inst.churnTarget > 0 && now >= inst.churnPulseAt && inst.lastPhaseColor) {
            inst.churnPulseAt = now + 600 + Math.random() * 250;
            jets(inst, Math.random() * Math.PI * 2, 1, inst.lastPhaseColor, inst.palette[(Math.random() * 5) | 0], 0.8);
        }
    }

    step(inst, dt, drainT);
    render(inst, drainT);

    if (inst.energyPending > 0 && now - inst.energyAt >= ENERGY_INTERVAL_MS) {
        const e = inst.energyPending;
        inst.energyPending = 0;
        inst.energyAt = now;
        try { inst.onEnergy?.(clamp01(e)); } catch { /* the sound is the caller's business */ }
    }

    if (inst.draining && drainT >= 1) teardown(inst.id, true);
}

function bindOutput(gl, target) {
    gl.bindFramebuffer(gl.FRAMEBUFFER, target.fbo);
    gl.viewport(0, 0, target.w, target.h);
}

function bindTexture(gl, unit, target) {
    gl.activeTexture(gl.TEXTURE0 + unit);
    gl.bindTexture(gl.TEXTURE_2D, target.tex);
    return unit;
}

function splat(inst, field, x, y, dir, radius, stretch, value) {
    const { gl, programs: { splat: p } } = inst;
    gl.useProgram(p.program);
    gl.uniform1i(p.u.uTarget, bindTexture(gl, 0, field.read));
    gl.uniform1f(p.u.uAspect, inst.aspect);
    gl.uniform2f(p.u.uPoint, x, y);
    gl.uniform2f(p.u.uDir, dir[0], dir[1]);
    gl.uniform1f(p.u.uRadius, radius);
    gl.uniform1f(p.u.uStretch, stretch);
    gl.uniform4fv(p.u.uValue, value);
    bindOutput(gl, field.write);
    gl.drawArrays(gl.TRIANGLES, 0, 3);
    field.swap();
}

/** Two invisible spoons on slow Lissajous paths, so the water never goes still. */
function stir(inst, dt) {
    const at = (t, fx, fy, ph) => [
        0.5 + Math.sin(t * fx + ph) * 0.28,
        0.5 + Math.sin(t * fy + ph * 1.7) * 0.24
    ];
    for (const [fx, fy, ph, force] of [[0.66, 0.53, 0, 1], [0.41, 0.6, 2.4, 0.8]]) {
        const [x, y] = at(inst.time, fx, fy, ph);
        const [nx, ny] = at(inst.time + 0.05, fx, fy, ph);
        const vx = (nx - x) * inst.aspect;
        const vy = ny - y;
        const len = Math.hypot(vx, vy) || 1;
        const push = STIR_FORCE * force * (1 + inst.churn * 1.5) * dt;
        splat(inst, inst.targets.velocity, x, y, [vx / len, vy / len], 0.02, 0.6,
            [vx / len * push, vy / len * push, 0, 0]);
    }
}

function runEmitters(inst, now, dt) {
    const { velocity, dye } = inst.targets;
    const scale = velocity.read.h / SIM_RES;
    inst.emitters = inst.emitters.filter((em) => {
        if (now < em.at) return true;
        const t = (now - em.at) / em.duration;
        if (t >= 1) return false;
        if (!em.announced) {
            em.announced = true;
            queueEnergy(inst, em.strength);
        }
        // The nozzle travels inward a little as it fires, and the jet tapers in and out.
        const travel = 0.10 * t;
        const x = em.x + em.dir[0] / inst.aspect * travel;
        const y = em.y + em.dir[1] * travel;
        const taper = Math.sin(Math.PI * Math.min(1, t * 1.15 + 0.05));
        const v = em.speed * scale * taper;
        splat(inst, velocity, x, y, em.dir, 0.0016, 0.35, [em.dir[0] * v * dt * 7, em.dir[1] * v * dt * 7, 0, 0]);
        const a = em.amount * taper * dt * 6;
        splat(inst, dye, x, y, em.dir, 0.0011, 0.4, [em.color[0] * a, em.color[1] * a, em.color[2] * a, a]);
        return true;
    });
}

function stirFromPointer(inst, dt, now) {
    const p = inst.pointer;
    if (!p.moved) return;
    p.moved = false;
    const rect = inst.canvas.getBoundingClientRect();
    if (rect.width < 1 || rect.height < 1) return;
    const u = (p.x - rect.left) / rect.width;
    const v = 1 - (p.y - rect.top) / rect.height;
    const prev = p.prevU;
    p.prevU = [u, v, now];
    if (!prev || now - prev[2] > 200) return;
    if (u < -0.05 || u > 1.05 || v < -0.05 || v > 1.05) return;

    const du = (u - prev[0]) * inst.aspect;
    const dv = v - prev[1];
    const dist = Math.hypot(du, dv);
    if (dist < 1e-4) return;
    const { velocity, dye } = inst.targets;
    const dir = [du / dist, dv / dist];
    // uv per second → sim texels per second, capped so a flick stirs rather than detonates.
    const speed = Math.min(POINTER_MAX, (dist / Math.max(dt, 1 / 120)) * velocity.read.h * 0.5);
    splat(inst, velocity, u, v, dir, 0.0012, 0.5, [dir[0] * speed, dir[1] * speed, 0, 0]);
    const color = inst.lastPhaseColor ?? inst.palette[0];
    const a = speed / POINTER_MAX * 0.22;
    splat(inst, dye, u, v, dir, 0.0005, 0.5, [color[0] * a, color[1] * a, color[2] * a, a]);
    queueEnergy(inst, speed / POINTER_MAX * 0.5);
}

function swirl(inst, dt) {
    const { gl, programs: { swirl: p } } = inst;
    const { velocity } = inst.targets;
    gl.useProgram(p.program);
    gl.uniform1i(p.u.uVelocity, bindTexture(gl, 0, velocity.read));
    gl.uniform1f(p.u.uAspect, inst.aspect);
    gl.uniform1f(p.u.uDt, dt);
    gl.uniform1f(p.u.uSwirl, 420 * inst.churn * (velocity.read.h / SIM_RES));
    gl.uniform1f(p.u.uTime, inst.time);
    bindOutput(gl, velocity.write);
    gl.drawArrays(gl.TRIANGLES, 0, 3);
    velocity.swap();
}

function step(inst, dt, drainT) {
    const { gl, programs: P, targets: T } = inst;
    const { velocity, pressure, divergence, curl, dye } = T;
    const texel = velocity.read.texel;

    gl.useProgram(P.curl.program);
    gl.uniform2fv(P.curl.u.uTexel, texel);
    gl.uniform1i(P.curl.u.uVelocity, bindTexture(gl, 0, velocity.read));
    bindOutput(gl, curl);
    gl.drawArrays(gl.TRIANGLES, 0, 3);

    gl.useProgram(P.vorticity.program);
    gl.uniform2fv(P.vorticity.u.uTexel, texel);
    gl.uniform1i(P.vorticity.u.uVelocity, bindTexture(gl, 0, velocity.read));
    gl.uniform1i(P.vorticity.u.uCurl, bindTexture(gl, 1, curl));
    gl.uniform1f(P.vorticity.u.uCurlStrength, inst.curl);
    gl.uniform1f(P.vorticity.u.uDt, dt);
    bindOutput(gl, velocity.write);
    gl.drawArrays(gl.TRIANGLES, 0, 3);
    velocity.swap();

    gl.useProgram(P.divergence.program);
    gl.uniform2fv(P.divergence.u.uTexel, texel);
    gl.uniform1i(P.divergence.u.uVelocity, bindTexture(gl, 0, velocity.read));
    bindOutput(gl, divergence);
    gl.drawArrays(gl.TRIANGLES, 0, 3);

    // Warm-start the solve from last frame's pressure, decayed: 20 iterations then converge
    // far better than 20 from zero.
    gl.useProgram(P.scale.program);
    gl.uniform1i(P.scale.u.uTexture, bindTexture(gl, 0, pressure.read));
    gl.uniform1f(P.scale.u.uValue, PRESSURE_DECAY);
    bindOutput(gl, pressure.write);
    gl.drawArrays(gl.TRIANGLES, 0, 3);
    pressure.swap();

    gl.useProgram(P.pressure.program);
    gl.uniform2fv(P.pressure.u.uTexel, texel);
    gl.uniform1i(P.pressure.u.uDivergence, bindTexture(gl, 1, divergence));
    for (let i = 0; i < PRESSURE_ITERATIONS; i++) {
        gl.uniform1i(P.pressure.u.uPressure, bindTexture(gl, 0, pressure.read));
        bindOutput(gl, pressure.write);
        gl.drawArrays(gl.TRIANGLES, 0, 3);
        pressure.swap();
    }

    gl.useProgram(P.gradient.program);
    gl.uniform2fv(P.gradient.u.uTexel, texel);
    gl.uniform1i(P.gradient.u.uPressure, bindTexture(gl, 0, pressure.read));
    gl.uniform1i(P.gradient.u.uVelocity, bindTexture(gl, 1, velocity.read));
    bindOutput(gl, velocity.write);
    gl.drawArrays(gl.TRIANGLES, 0, 3);
    velocity.swap();

    // Velocity advects itself, then carries the dye.
    const A = P.advect;
    gl.useProgram(A.program);
    gl.uniform2fv(A.u.uSimTexel, texel);
    gl.uniform1f(A.u.uDt, dt);
    gl.uniform1i(A.u.uVelocity, bindTexture(gl, 0, velocity.read));
    gl.uniform1i(A.u.uSource, bindTexture(gl, 1, velocity.read));
    gl.uniform1f(A.u.uDissipation, VELOCITY_DISSIPATION + drainT * 3);
    gl.uniform1f(A.u.uEdgeFade, 0);
    bindOutput(gl, velocity.write);
    gl.drawArrays(gl.TRIANGLES, 0, 3);
    velocity.swap();

    gl.uniform1i(A.u.uVelocity, bindTexture(gl, 0, velocity.read));
    gl.uniform1i(A.u.uSource, bindTexture(gl, 1, dye.read));
    gl.uniform1f(A.u.uDissipation, DYE_DISSIPATION * (1 - 0.4 * inst.churn) + drainT * drainT * 4);
    gl.uniform1f(A.u.uEdgeFade, 2.5);
    bindOutput(gl, dye.write);
    gl.drawArrays(gl.TRIANGLES, 0, 3);
    dye.swap();
}

function render(inst, drainT) {
    const { gl, surface, programs: { display: p } } = inst;
    const dye = inst.targets.dye.read;
    surface.bindTarget();
    gl.useProgram(p.program);
    gl.uniform1i(p.u.uDye, bindTexture(gl, 0, dye));
    gl.uniform2fv(p.u.uDyeTexel, dye.texel);
    gl.uniform1f(p.u.uDark, inst.dark ? 1 : 0);
    // The last stretch of the drain fades to guaranteed nothing, whatever the sim left behind.
    gl.uniform1f(p.u.uFade, 1 - Math.pow(drainT, 2.2));
    gl.uniform1f(p.u.uAspect, inst.aspect);
    gl.uniform1f(p.u.uDrain, drainT);
    gl.drawArrays(gl.TRIANGLES, 0, 3);
    surface.present();
}

function queueEnergy(inst, e) {
    if (!inst.onEnergy) return;
    inst.energyPending = Math.max(inst.energyPending, clamp01(e));
}

// ── Teardown ─────────────────────────────────────────────────────────────────────────────

/**
 * Every exit funnels here. `natural` = the effect ended on its own (drain finished, tier drop,
 * lost context, detached canvas): a drain in flight then still reports onDrained, so the comic's
 * reveal is never held hostage by a GPU hiccup. An explicit stop() does not.
 */
function teardown(id, natural) {
    const inst = instances.get(id);
    if (!inst) return;
    instances.delete(id);

    inst.task?.();
    window.removeEventListener('pointermove', inst.onPointer);
    inst.canvas.removeEventListener('webglcontextlost', inst.onLost);

    const { gl, canvas } = inst;
    try {
        if (!gl.isContextLost()) {
            destroyTargets(inst);
            for (const p of Object.values(inst.programs)) gl.deleteProgram(p.program);
            gl.deleteVertexArray(inst.vao);
            gl.bindFramebuffer(gl.FRAMEBUFFER, null);
            gl.clearColor(0, 0, 0, 0);
            gl.clear(gl.COLOR_BUFFER_BIT);
        }
        // Shrinking drops the backing store (and clears it). A detached canvas will never be
        // reused, so its context is handed back too; a live one keeps it so a regenerate can
        // start again on the same element — a released context cannot be re-acquired.
        canvas.width = canvas.height = 1;
        if (!canvas.isConnected) inst.surface.release();
    } catch {
        // The loop is already stopped, which is the part that matters.
    }

    if (natural && inst.draining) {
        try { inst.onDrained?.(); } catch { /* caller's problem, not ours */ }
    }
}

gfx.onTierChanged((tier) => {
    if (tier === 'full') return;
    for (const id of [...instances.keys()]) teardown(id, true);
});
