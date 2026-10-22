// The comic is printed onto the page: paper, then four process plates, then the ink dries.
//
// THE MOMENT THIS IS FOR. Same moment as comic-reveal.js — the strip the user just waited ten
// seconds for arriving — but at the `full` tier it gets a press run instead of a wipe. A sheet of
// paper feeds in, then the yellow, magenta and cyan plates roll on as rotated halftone screens
// (classic angles Y0 M75 C15 K45), each starting mis-registered and sliding into register while
// its dots grow, and the key plate lands last and snaps the line art in. Panel by panel, top to
// bottom, because a press roller travels and the strip's panels are stacked vertically. The wet
// ink glistens and dries off, the dots melt into the real artwork, and the canvas hands over to
// the real <img>.
//
// WHY A SHADER AND NOT CSS. The plates are the image's own CMYK separation, so the effect has to
// read the pixels. The display src is a cross-origin SAS link with no CORS, so the caller passes
// the same-origin copy (`comicImageUrl(placeId)`), which this module decodes into a texture.
//
// HOW THE REAL IMAGE STAYS HIDDEN. The container's existing ink-development mask (app.css) is
// pinned shut — `data-comic-reveal` + `--comic-reveal: 0` — and the canvas sits OUTSIDE the
// container (the mask would hide it too), absolutely positioned over the container's box in the
// tilt wrapper. It mirrors the container's transform, opacity, radius and frame each frame, so
// the entrance animation and the tilt keep working underneath it.
//
// THE HANDOVER. For the last 300ms the shader crossfades the halftone into the plain texture,
// then the mask is lifted and the canvas fades out over the now-visible <img> for ~120ms before it
// is removed. Doing the final step on the compositor makes the last frame the real element, not a
// resample of it at a capped DPR — a texture can match an <img> closely, never exactly.
//
// COST. One fullscreen triangle over the strip, DPR capped at 1.5. Per pixel: 2x2 neighbouring
// halftone cells for each of three colour plates (12 mip-averaged taps), one full-res tap for the
// key plate's line art, one for the final crossfade. About 1.8s, then nothing.
//
// FALLBACKS. Anything short of a working press resolves 0 with the container back to normal, so
// the caller runs the CSS mask reveal instead: tier below `full`, no WebGL2, a shader failure, or
// the image not decoding within 2.5s. Once running, a tier drop, a lost context or a detached
// container finishes immediately with the comic fully visible.

import {
    gfx,
    createSurface,
    compileProgram,
    FULLSCREEN_VERTEX_SHADER,
    loadImage,
    createTexture
} from './gfx-core.js';

const PROPERTY = '--comic-reveal';
const ATTRIBUTE = 'comicReveal';   // dataset key for data-comic-reveal
const LOAD_TIMEOUT_MS = 2500;
const MAX_DPR = 1.5;

// The press schedule, in ms. Shared by the shader (interpolated into its source) and the band cues.
const T = {
    paper: 240,          // the sheet feeds in
    first: 170,          // first impression at the top edge
    bandSpan: 620,       // the whole stagger from the first band to the last
    inner: 0.55,         // share of one band's slot the roller spends crossing that band
    plateY: 0,           // plate order and offsets within one pixel's run
    plateM: 115,
    plateC: 230,
    plateK: 360,
    ramp: 380,           // Y/M/C dot growth
    rampK: 240,          // the key plate is quicker: it snaps
    dry: 480,            // wet sheen after the key plate lands
    settleStart: 1440,   // halftone -> exact artwork
    settleEnd: 1700,
    fade: 120            // canvas fades over the real <img>
};

const FRAGMENT_SHADER = `#version 300 es
precision highp float;
in vec2 vUv;
out vec4 outColor;

uniform sampler2D uImage;
uniform vec2  uRes;       // canvas, device px
uniform float uDpr;       // device px per CSS px
uniform vec4  uRect;      // the <img> box inside the canvas, CSS px, top-left origin
uniform float uTime;      // ms since the press started
uniform float uBands;
uniform float uStagger;   // ms between bands
uniform float uCell;      // halftone cell, CSS px
uniform float uLod;       // mip level that averages about one cell
uniform float uSettle;    // 0..1 halftone -> exact texture
uniform float uMarks;     // registration marks visibility
uniform vec3  uPaper;
uniform vec3  uFilm;      // the container's letterbox colour around the image
uniform vec3  uGlow;      // dark theme only: the press lamp at the roller
uniform float uDark;

const float PI = 3.14159265;
const float PAPER_MS = ${T.paper.toFixed(1)};
const float FIRST    = ${T.first.toFixed(1)};
const float INNER    = ${T.inner.toFixed(3)};
const float RAMP     = ${T.ramp.toFixed(1)};
const float RAMP_K   = ${T.rampK.toFixed(1)};
const float OFF_Y    = ${T.plateY.toFixed(1)};
const float OFF_M    = ${T.plateM.toFixed(1)};
const float OFF_C    = ${T.plateC.toFixed(1)};
const float OFF_K    = ${T.plateK.toFixed(1)};
const float DRY      = ${T.dry.toFixed(1)};

// Process inks as transmission filters: multiplied over paper, they overprint like real ink —
// cyan over yellow is green without anyone choosing a green.
const vec3 INK_C = vec3(0.10, 0.70, 0.93);
const vec3 INK_M = vec3(0.93, 0.17, 0.58);
const vec3 INK_Y = vec3(1.00, 0.94, 0.12);
const vec3 INK_K = vec3(0.11, 0.10, 0.13);

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

mat2 rot(float a) {
    float c = cos(a), s = sin(a);
    return mat2(c, s, -s, c);
}

vec3 art(vec2 uv, float lod) {
    return textureLod(uImage, vec2(uv.x, 1.0 - uv.y), lod).rgb;
}

vec4 cmyk(vec3 c) {
    float k = 1.0 - max(c.r, max(c.g, c.b));
    vec3 cmy = (1.0 - c - k) / max(1.0 - k, 1e-3);
    return vec4(clamp(cmy, 0.0, 1.0), k);
}

// A plate that has not landed yet: shifted, scaled and turned about the image centre, by an
// amount m that springs from 1 to 0 (with one small overshoot) as the plate comes into register.
vec2 misregister(vec2 p, vec2 offset, float scale, float turn, float m) {
    vec2 centre = uRect.zw * 0.5;
    return centre + rot(turn * m) * ((p - centre - offset * m) / (1.0 + scale * m));
}

float springIn(float x) {
    return exp(-4.2 * x) * cos(9.0 * x) * (1.0 - smoothstep(0.82, 1.0, x));
}

// One plate's dot coverage at q (plate space, CSS px). The four nearest screen cells are tested
// so a dot past 50% can grow into its neighbours' corners and the screen fills solid.
float screen(vec2 q, float angle, int channel, float cover, float gain, float edge, float aa) {
    mat2 r = rot(angle);
    vec2 g = (r * q) / uCell;
    vec2 base = floor(g - 0.5);
    float a = 0.0;
    for (int j = 0; j < 2; j++) {
        for (int i = 0; i < 2; i++) {
            vec2 c = base + vec2(float(i), float(j)) + 0.5;
            vec2 world = transpose(r) * (c * uCell);
            float k = cmyk(art(world / uRect.zw, uLod))[channel];
            if (channel == 3) {
                // Solid black belongs to the line plate. A cell that only averages grey because a
                // line runs through it would ring every line with dots, so the key screen keeps
                // only cells whose own centre agrees with their average: flat greys.
                float fine = cmyk(art(world / uRect.zw, 0.0)).w;
                k = min(k, fine) * (1.0 - smoothstep(0.62, 0.8, k)) * (1.0 - smoothstep(0.5, 0.65, fine));
            }
            k *= cover;
            float radius = (uCell * sqrt(k / PI) * (1.0 + 0.28 * k * k) + gain) * smoothstep(0.015, 0.07, k);
            // The ragged edge never reaches the centre of an empty cell: no dot, no speck.
            float d = max(length(g - c) * uCell + edge, 0.0);
            a = max(a, (1.0 - smoothstep(radius - aa, radius + aa, d)) * smoothstep(0.0, 0.7, radius));
        }
    }
    return a;
}

// A registration target near each corner, printed by every plate with that plate's own error, so
// "sliding into register" is something you can see happen.
float marks(vec2 q) {
    vec2 inset = vec2(14.0);
    vec2 c = vec2(q.x < uRect.z * 0.5 ? inset.x : uRect.z - inset.x,
                  q.y < uRect.w * 0.5 ? inset.y : uRect.w - inset.y);
    vec2 d = q - c;
    float ring = 1.0 - smoothstep(0.55, 1.15, abs(length(d) - 5.5));
    float cross = (1.0 - smoothstep(0.4, 1.0, abs(d.x))) * step(abs(d.y), 9.0)
                + (1.0 - smoothstep(0.4, 1.0, abs(d.y))) * step(abs(d.x), 9.0);
    return clamp(ring + cross, 0.0, 1.0);
}

vec3 paperAt(vec2 p) {
    // Long fibres, a fine tooth and soft mottling. Stretched along x: newsprint's grain direction.
    float fibre = vnoise(p * vec2(0.11, 0.85));
    float ridge = 1.0 - abs(2.0 * vnoise(p * vec2(0.045, 0.32) + 17.0) - 1.0);
    float strands = smoothstep(0.93, 0.995, ridge);
    float tooth = hash(floor(p * 1.5));
    float mottle = vnoise(p * 0.018 + 3.0);
    vec3 c = uPaper;
    c *= 1.0 - 0.035 * fibre - 0.022 * tooth - 0.035 * mottle;
    c *= 1.0 - 0.06 * strands;
    return c;
}

void main() {
    vec2 px = vec2(gl_FragCoord.x, uRes.y - gl_FragCoord.y) / uDpr;    // CSS px, top-left
    vec2 canvasCss = uRes / uDpr;
    vec2 p = px - uRect.xy;
    vec2 v = p / uRect.zw;
    float inside = step(0.0, v.x) * step(v.x, 1.0) * step(0.0, v.y) * step(v.y, 1.0);

    // The sheet feeds in from the top, with a soft shadow under its leading edge.
    float feed = clamp(uTime / PAPER_MS, 0.0, 1.0);
    feed = 1.0 - pow(1.0 - feed, 3.0);
    float front = feed * 1.2 - 0.1;
    float yc = px.y / canvasCss.y;
    float sheet = 1.0 - smoothstep(front - 0.025, front, yc);
    float lip = exp(-pow((yc - front + 0.03) / 0.03, 2.0)) * (1.0 - feed);

    vec3 exact = art(v, -0.25);
    vec3 col;

    if (inside > 0.5) {
        // The roller crosses the strip band by band (one band per stacked panel), with a slightly
        // ragged front so it reads as ink rather than a progress bar.
        float band = clamp(floor(v.y * uBands), 0.0, uBands - 1.0);
        float yIn = clamp(v.y * uBands - band, 0.0, 1.0);
        float delay = FIRST + (band + INNER * yIn) * uStagger + (vnoise(vec2(p.x * 0.03, band * 7.0)) - 0.5) * 16.0;
        float lt = uTime - delay;

        float lpY = clamp((lt - OFF_Y) / RAMP, 0.0, 1.0);
        float lpM = clamp((lt - OFF_M) / RAMP, 0.0, 1.0);
        float lpC = clamp((lt - OFF_C) / RAMP, 0.0, 1.0);
        float lpK = clamp((lt - OFF_K) / RAMP_K, 0.0, 1.0);

        // Registration lags the dots, so a plate is visibly off while it inks up.
        float mY = springIn(clamp((lt - OFF_Y) / (RAMP * 1.45), 0.0, 1.0));
        float mM = springIn(clamp((lt - OFF_M) / (RAMP * 1.45), 0.0, 1.0));
        float mC = springIn(clamp((lt - OFF_C) / (RAMP * 1.45), 0.0, 1.0));
        float mK = springIn(clamp((lt - OFF_K) / (RAMP_K * 1.6), 0.0, 1.0));

        vec2 qY = misregister(p, vec2(-9.0,  6.0),  0.022,  0.010, mY);
        vec2 qM = misregister(p, vec2( 8.0, -7.0), -0.018, -0.012, mM);
        vec2 qC = misregister(p, vec2(-6.0, -9.0),  0.015,  0.008, mC);
        vec2 qK = misregister(p, vec2( 6.0,  8.0), -0.012, -0.006, mK);

        float wet = smoothstep(0.0, 60.0, lt) * (1.0 - smoothstep(0.0, DRY, lt - OFF_K - RAMP_K));
        float edge = (vnoise(p * 0.9) - 0.5) * (0.7 + 0.6 * wet);    // ragged, bleeding dot edges
        float aa = 0.65 / uDpr + 0.25 + 0.25 * wet;
        float gain = 0.06 + 0.2 * wet;                                  // dot gain while the ink spreads

        float cY = smoothstep(0.0, 1.0, lpY);
        float cM = smoothstep(0.0, 1.0, lpM);
        float cC = smoothstep(0.0, 1.0, lpC);
        float cK = smoothstep(0.0, 1.0, lpK) + 0.22 * sin(PI * lpK);   // the key plate bites, then relaxes

        float aY = screen(qY, 0.0,           2, cY, gain, edge, aa);
        float aM = screen(qM, radians(75.0), 1, cM, gain, edge, aa);
        float aC = screen(qC, radians(15.0), 0, cC, gain, edge, aa);
        float aK = screen(qK, radians(45.0), 3, cK, gain, edge, aa);

        // Line art: the key plate's solid black thickens from the cores of the lines outward.
        float kLine = cmyk(texture(uImage, vec2(qK.x / uRect.z, 1.0 - qK.y / uRect.w)).rgb).w;
        float thr = mix(1.08, 0.72, clamp(cK, 0.0, 1.0));
        aK = max(aK, smoothstep(thr - 0.07, thr + 0.07, kLine + edge * 0.05) * step(0.001, lpK));

        float mv = uMarks;
        aY = max(aY, marks(qY) * cY * mv);
        aM = max(aM, marks(qM) * cM * mv);
        aC = max(aC, marks(qC) * cC * mv);
        aK = max(aK, marks(qK) * clamp(cK, 0.0, 1.0) * mv);

        vec3 paper = paperAt(p);
        // Ink takes less well on the high points of the fibre.
        float take = 0.9 + 0.1 * vnoise(p * 1.7 + 5.0);
        col = paper;
        col *= mix(vec3(1.0), INK_Y, aY * take);
        col *= mix(vec3(1.0), INK_M, aM * take);
        col *= mix(vec3(1.0), INK_C, aC * take);
        col *= mix(vec3(1.0), INK_K, aK * take);

        // Wet ink sits darker and glossy; the thickness of the ink is the height field the gloss
        // is lit from, so the dots bead up at their edges and the shine dries off with them.
        float inkAmt = 1.0 - dot(col / max(paper, vec3(1e-3)), vec3(0.299, 0.587, 0.114));
        col *= 1.0 - 0.12 * wet * inkAmt;
        vec2 grad = vec2(dFdx(inkAmt), dFdy(inkAmt)) * uDpr;
        vec3 n = normalize(vec3(-grad * 2.4, 1.0));
        vec3 h = normalize(normalize(vec3(-0.45, 0.6, 0.75)) + vec3(0.0, 0.0, 1.0));
        // Minus what flat paper would catch, so only ink edges glint.
        float spec = max(pow(max(dot(n, h), 0.0), 36.0) - pow(h.z, 36.0), 0.0) * wet;
        float streak = pow(max(sin(dot(p, vec2(0.011, 0.019)) - uTime * 0.0065), 0.0), 14.0);
        col += vec3(spec * 0.3 + streak * wet * inkAmt * 0.16);

        // The impression line: a roller shadow just ahead of the key plate, a bright rim behind.
        float ahead = exp(-pow((lt - OFF_K + 12.0) / 16.0, 2.0));
        float behind = exp(-pow((lt - OFF_K - 30.0) / 24.0, 2.0));
        col *= 1.0 - (0.16 - 0.06 * uDark) * ahead;
        col += (1.0 - uDark) * 0.08 * behind;
        col += uDark * uGlow * (0.42 * behind + 0.18 * ahead);

        col = mix(col, exact, uSettle);
    } else {
        col = uFilm;
    }

    col *= 1.0 - 0.25 * lip * inside;
    col = clamp(col, 0.0, 1.0);
    float alpha = mix(sheet, 1.0, uSettle);
    outColor = vec4(col * alpha, alpha);
}`;

const instances = new Map();
let nextId = 1;

function parseColor(value, fallback) {
    const hex = (value || '').trim().replace('#', '');
    const full = hex.length === 3 ? hex.split('').map((c) => c + c).join('') : hex;
    if (!/^[0-9a-f]{6}$/i.test(full)) return fallback;
    return [0, 2, 4].map((i) => parseInt(full.slice(i, i + 2), 16) / 255);
}

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

/** Paper is warmed from the card token; in dark mode it is a dim sheet, not the dark card itself. */
function readPalette() {
    const style = getComputedStyle(document.documentElement);
    const card = parseColor(style.getPropertyValue('--color-card'), [1, 1, 1]);
    const film = parseColor(style.getPropertyValue('--surface-film'), [0.04, 0.04, 0.04]);
    const accent = parseColor(style.getPropertyValue('--color-accent'), [0.98, 0.75, 0.14]);
    const dark = isDark();
    const newsprint = [0.965, 0.935, 0.86];
    const share = dark ? 0.88 : 0.55;
    const paper = card.map((c, i) => c * (1 - share) + newsprint[i] * share);
    return { paper, film, glow: accent, dark };
}

function hide(container) {
    container.dataset[ATTRIBUTE] = 'running';
    container.style.setProperty(PROPERTY, '0');
}

function unhide(container) {
    try {
        // Attribute BEFORE property, as in comic-reveal.js: a masked element with no reveal value
        // would resolve to the @property initial value for a frame.
        delete container.dataset[ATTRIBUTE];
        container.style.removeProperty(PROPERTY);
    } catch {
        // Already detached.
    }
}

/**
 * Prints the comic onto the page. Full tier only.
 * @param {HTMLElement} container the .comic-strip-container (its parent is the tilt wrapper)
 * @param {{ src?: string, bands?: number, onBand?: (index: number, total: number) => void,
 *           onDone?: (completed: boolean) => void }} options
 * @returns {Promise<number>} handle, or 0 when no press ran (the container is then untouched)
 */
export async function start(container, options = {}) {
    let instance = null;
    try {
        if (!container || !container.parentElement || !gfx.allows('full')) return 0;
        const src = typeof options.src === 'string' ? options.src : '';
        if (!src) return 0;

        // A regenerate swaps the src on the same element: one press per container.
        for (const [existingId, existing] of instances) {
            if (existing.container === container) finish(existingId);
        }

        instance = {
            id: nextId++,
            container,
            parent: container.parentElement,
            image: container.querySelector('img'),
            bands: Math.max(1, Math.min(4, Math.round(options.bands ?? 2))),
            onBand: typeof options.onBand === 'function' ? options.onBand : null,
            onDone: typeof options.onDone === 'function' ? options.onDone : null,
            palette: readPalette(),
            canvas: null,
            surface: null,
            program: null,
            vao: null,
            texture: null,
            uniforms: null,
            onLost: null,
            stop: null,
            restorePosition: false,
            box: '',
            mirrored: {},
            opacity: 1,
            t0: -1,
            bandsAnnounced: 0,
            handedOver: false,
            started: false,
            done: false
        };
        instances.set(instance.id, instance);

        hide(container);
        if (!buildPress(instance)) {
            teardown(instance, false);
            return 0;
        }

        const image = await loadImage(src, LOAD_TIMEOUT_MS);
        if (instance.done) return 0;   // finished while loading: tier drop, context loss, a newer start
        if (!image || !container.isConnected || !gfx.allows('full')) {
            teardown(instance, false);
            return 0;
        }

        const gl = instance.surface.gl;
        instance.texture = createTexture(gl, image, { mipmaps: true });
        instance.textureWidth = image.naturalWidth || image.width || 1;
        instance.started = true;
        instance.stop = gfx.addTask(`halftone-reveal#${instance.id}`, (now) => tick(instance, now));
        return instance.id;
    } catch {
        if (instance) teardown(instance, false);
        return 0;
    }
}

/** Ends the press with the comic fully visible. Also the abort path. Idempotent. */
export function finish(handle) {
    try {
        const instance = instances.get(handle);
        if (instance) teardown(instance, false);
    } catch {
        // Nothing more to undo.
    }
}

function buildPress(instance) {
    const { parent, container } = instance;

    // The canvas is positioned against the tilt wrapper, so it must be a containing block.
    if (getComputedStyle(parent).position === 'static') {
        parent.style.position = 'relative';
        instance.restorePosition = true;
    }

    const canvas = document.createElement('canvas');
    canvas.className = 'halftone-reveal-canvas';
    canvas.setAttribute('aria-hidden', 'true');
    Object.assign(canvas.style, {
        position: 'absolute',
        display: 'block',
        pointerEvents: 'none',
        margin: '0',
        left: '0px',
        top: '0px',
        width: '1px',
        height: '1px'
    });
    // After the container, so it paints above it.
    parent.insertBefore(canvas, container.nextSibling);
    instance.canvas = canvas;
    syncBox(instance);

    const surface = createSurface(canvas, { maxDpr: MAX_DPR });
    if (!surface) return false;
    instance.surface = surface;
    const gl = surface.gl;

    instance.onLost = (event) => {
        event.preventDefault();
        finish(instance.id);
    };
    canvas.addEventListener('webglcontextlost', instance.onLost);

    instance.program = compileProgram(gl, FULLSCREEN_VERTEX_SHADER, FRAGMENT_SHADER);
    instance.vao = gl.createVertexArray();
    const names = ['uImage', 'uRes', 'uDpr', 'uRect', 'uTime', 'uBands', 'uStagger', 'uCell', 'uLod',
        'uSettle', 'uMarks', 'uPaper', 'uFilm', 'uGlow', 'uDark'];
    instance.uniforms = Object.fromEntries(names.map((n) => [n, gl.getUniformLocation(instance.program, n)]));
    return true;
}

/** Keeps the canvas over the container's layout box and mirrors what the container paints itself. */
function syncBox(instance) {
    const { container, canvas } = instance;
    const box = `${container.offsetLeft},${container.offsetTop},${container.offsetWidth},${container.offsetHeight}`;
    if (box !== instance.box) {
        instance.box = box;
        Object.assign(canvas.style, {
            left: `${container.offsetLeft}px`,
            top: `${container.offsetTop}px`,
            width: `${container.offsetWidth}px`,
            height: `${container.offsetHeight}px`
        });
    }

    // The entrance animation (scale + opacity + frame colour) and the radius live on the
    // container; the frame is a box-shadow the mask hides, so the canvas carries a copy.
    const cs = getComputedStyle(container);
    for (const [key, value] of [
        ['transform', cs.transform],
        ['borderRadius', cs.borderRadius],
        ['boxShadow', cs.boxShadow]
    ]) {
        if (instance.mirrored[key] !== value) {
            instance.mirrored[key] = value;
            canvas.style[key] = value;
        }
    }
    instance.opacity = parseFloat(cs.opacity);
    if (!Number.isFinite(instance.opacity)) instance.opacity = 1;
}

function imageRect(instance) {
    const { container, image } = instance;
    if (image && image.offsetParent === container && image.offsetWidth > 0 && image.offsetHeight > 0) {
        return [image.offsetLeft, image.offsetTop, image.offsetWidth, image.offsetHeight];
    }
    return [0, 0, container.offsetWidth || 1, container.offsetHeight || 1];
}

function smooth(edge0, edge1, x) {
    const t = Math.min(1, Math.max(0, (x - edge0) / (edge1 - edge0)));
    return t * t * (3 - 2 * t);
}

function tick(instance, now) {
    if (instance.done) return;
    try {
        const { container, canvas, surface } = instance;
        // A navigation can remove either element mid-run.
        if (!container.isConnected || !canvas.isConnected) {
            teardown(instance, false);
            return;
        }
        if (instance.t0 < 0) instance.t0 = now;
        const t = now - instance.t0;

        syncBox(instance);
        if (!surface.beginFrame()) return;
        draw(instance, t);

        announceBands(instance, t);

        if (t >= T.settleEnd) {
            // The canvas now shows the plain artwork: lift the mask under it and fade the copy off.
            if (!instance.handedOver) {
                instance.handedOver = true;
                unhide(container);
            }
            const fade = Math.min(1, (t - T.settleEnd) / T.fade);
            canvas.style.opacity = String(instance.opacity * (1 - fade));
            if (fade >= 1) teardown(instance, true);
        } else {
            canvas.style.opacity = String(instance.opacity);
        }
    } catch {
        teardown(instance, false);
    }
}

function draw(instance, t) {
    const { surface, uniforms: u, palette } = instance;
    const gl = surface.gl;
    const cssWidth = Math.max(1, instance.canvas.clientWidth);
    const dpr = surface.width / cssWidth;
    const rect = imageRect(instance);

    // Cell size follows the strip's width so the screen reads as print at any size: ~6px at 640.
    const cell = Math.min(7, Math.max(4, rect[2] / 108));
    const lod = Math.max(0, Math.log2(Math.max(1, cell * (instance.textureWidth / rect[2]) * 0.7)));

    gl.useProgram(instance.program);
    gl.bindVertexArray(instance.vao);
    gl.activeTexture(gl.TEXTURE0);
    gl.bindTexture(gl.TEXTURE_2D, instance.texture);
    gl.uniform1i(u.uImage, 0);
    gl.uniform2f(u.uRes, surface.width, surface.height);
    gl.uniform1f(u.uDpr, dpr);
    gl.uniform4f(u.uRect, rect[0], rect[1], rect[2], rect[3]);
    gl.uniform1f(u.uTime, t);
    gl.uniform1f(u.uBands, instance.bands);
    gl.uniform1f(u.uStagger, T.bandSpan / instance.bands);
    gl.uniform1f(u.uCell, cell);
    gl.uniform1f(u.uLod, lod);
    gl.uniform1f(u.uSettle, smooth(T.settleStart, T.settleEnd, t));
    gl.uniform1f(u.uMarks, 1 - smooth(T.settleStart - 380, T.settleStart, t));
    gl.uniform3fv(u.uPaper, palette.paper);
    gl.uniform3fv(u.uFilm, palette.film);
    gl.uniform3fv(u.uGlow, palette.glow);
    gl.uniform1f(u.uDark, palette.dark ? 1 : 0);
    gl.drawArrays(gl.TRIANGLES, 0, 3);
    surface.present();
}

/**
 * Each band is announced at the key plate's impression across its middle — the moment the panel
 * turns from colour wash to finished line art, which is where a "thunk" belongs.
 */
function bandCueMs(index, bands) {
    return T.first + (index + T.inner * 0.5) * (T.bandSpan / bands) + T.plateK + T.rampK * 0.5;
}

function announceBands(instance, t) {
    while (instance.bandsAnnounced < instance.bands && t >= bandCueMs(instance.bandsAnnounced, instance.bands)) {
        const index = instance.bandsAnnounced++;
        if (instance.onBand) {
            try {
                instance.onBand(index, instance.bands);
            } catch {
                // A cue failing must not stall the press.
            }
        }
    }
}

function teardown(instance, completed) {
    if (instance.done) return;
    instance.done = true;
    instances.delete(instance.id);
    try { instance.stop?.(); } catch { /* already unregistered */ }

    // Same synchronous block, so the same frame: the real comic appears as the canvas goes.
    unhide(instance.container);
    const canvas = instance.canvas;
    if (canvas) {
        try {
            if (instance.onLost) canvas.removeEventListener('webglcontextlost', instance.onLost);
            canvas.remove();
        } catch { /* detached */ }
    }
    if (instance.restorePosition) {
        try { instance.parent.style.removeProperty('position'); } catch { /* detached */ }
    }

    const surface = instance.surface;
    if (surface && !surface.dead) {
        try {
            const gl = surface.gl;
            if (instance.texture) gl.deleteTexture(instance.texture);
            if (instance.vao) gl.deleteVertexArray(instance.vao);
            if (instance.program) gl.deleteProgram(instance.program);
        } catch { /* context already gone */ }
        surface.release();
    }

    if (instance.started && instance.onDone) {
        try {
            instance.onDone(completed);
        } catch {
            // The comic is visible either way.
        }
    }
}

gfx.onTierChanged(() => {
    // Below `full` the press stops where it is and the comic is simply there.
    if (!gfx.allows('full')) {
        for (const instance of [...instances.values()]) teardown(instance, false);
    }
});
