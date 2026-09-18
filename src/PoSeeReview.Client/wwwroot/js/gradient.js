// Strangeness-reactive background. A single fullscreen triangle running domain-warped value
// noise, lit by three moving lights, with hue, warp strength and speed driven by the current
// comic's score — so a 92 visibly seethes and a 30 drifts.
//
// LIGHTING, AND WHY IT IS AFFORDABLE HERE.
//
// There is no geometry in this scene. What there is instead is the noise field itself, read as a
// HEIGHT MAP: the same fbm that produces the colour also produces a surface, and a surface has a
// normal. That turns a flat colour ramp into something with form, for the cost of two extra fbm
// evaluations (central differences) rather than a mesh, a depth buffer, and a shadow map.
//
// Three lights, but only ONE casts a shadow. The key light's occlusion is a three-tap march
// along its direction against the height field — enough to darken the lee side of a ridge, which
// is all a viewer can perceive on a backdrop this soft. Shadowing all three would triple that
// cost to produce overlapping penumbrae nobody can see through the content sitting on top.
//
// Cost control, in order of how much it matters:
//   * DPR is capped at 1.25 here (lower than the shared default). This is an out-of-focus
//     backdrop; nobody can see the difference, and fill rate is the entire cost of the pass.
//   * The whole thing runs on the shared scheduler, so it pauses with the tab.
//   * 'full' tier only. At 'lite' the CSS gradient underneath is what shows.

import { gfx, createSurface, compileProgram, FULLSCREEN_VERTEX_SHADER } from './gfx-core.js';

const BACKDROP_UNIFORMS = `
uniform float uTime;
uniform vec2  uResolution;
uniform float uStrange;    // 0..1
uniform vec3  uColorA;
uniform vec3  uColorB;
uniform vec3  uColorC;
uniform vec3  uKeyLight;   // rgb tint of the shadow-casting light
uniform float uAudio;      // 0..1 overall level
uniform float uAudioBass;  // 0..1 low band
uniform float uAmbient;
`;

const BACKDROP_GLSL = `
float posee_hash(vec2 p) {
    p = fract(p * vec2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return fract(p.x * p.y);
}

float posee_valueNoise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    vec2 u = f * f * (3.0 - 2.0 * f);

    float a = posee_hash(i);
    float b = posee_hash(i + vec2(1.0, 0.0));
    float c = posee_hash(i + vec2(0.0, 1.0));
    float d = posee_hash(i + vec2(1.0, 1.0));

    return mix(mix(a, b, u.x), mix(c, d, u.x), u.y);
}

float posee_fbm(vec2 p) {
    float total = 0.0;
    float amplitude = 0.5;
    for (int i = 0; i < 3; i++) {
        total += posee_valueNoise(p) * amplitude;
        p *= 2.0;
        amplitude *= 0.5;
    }
    return total;
}

float posee_field(vec2 p, vec2 warp, float warpAmount, float t) {
    return posee_fbm(p * 2.1 + warp * warpAmount + t * 0.35);
}

struct PoseeScene {
    float energy;
    float t;
    float warpAmount;
    float aspect;
    vec2  centre;
};

PoseeScene posee_scene() {
    PoseeScene s;
    s.energy = uStrange + uAudio * 0.35;
    s.t = uTime * (0.02 + s.energy * 0.10);
    s.warpAmount = 0.15 + s.energy * 0.85;
    s.aspect = uResolution.x / max(uResolution.y, 1.0);
    s.centre = vec2(s.aspect, 1.0) * 0.5;
    return s;
}

vec2 posee_warp(vec2 p, PoseeScene s) {
    return vec2(
        posee_fbm(p * 1.4 + vec2(s.t, s.t * 0.7)),
        posee_fbm(p * 1.4 + vec2(-s.t * 0.8, s.t * 1.1) + 5.2)
    );
}

vec3 posee_backdrop(vec2 p, PoseeScene s) {
    vec2 warp = posee_warp(p, s);
    float n = posee_field(p, warp, s.warpAmount, s.t);
    float eps = 0.012;
    float hX = posee_field(p + vec2(eps, 0.0), warp, s.warpAmount, s.t);
    float hY = posee_field(p + vec2(0.0, eps), warp, s.warpAmount, s.t);
    vec3 normal = normalize(vec3((n - hX) / eps, (n - hY) / eps, 0.55));

    vec3 keyPos  = vec3(s.centre + vec2(cos(uTime * 0.13), sin(uTime * 0.17)) * 0.42, 0.55);
    vec3 fillPos = vec3(s.centre + vec2(cos(-uTime * 0.09 + 2.1), sin(-uTime * 0.11 + 2.1)) * 0.55, 0.40);
    vec3 rimPos  = vec3(s.centre + vec2(cos(uTime * 0.21 + 4.2), sin(uTime * 0.19 + 4.2)) * 0.68, 0.28);

    vec3 surface = vec3(p, n * 0.35);

    vec3 keyDir  = normalize(keyPos  - surface);
    vec3 fillDir = normalize(fillPos - surface);
    vec3 rimDir  = normalize(rimPos  - surface);

    float keyFall  = 1.0 / (1.0 + dot(keyPos.xy  - p, keyPos.xy  - p) * 2.2);
    float fillFall = 1.0 / (1.0 + dot(fillPos.xy - p, fillPos.xy - p) * 2.6);
    float rimFall  = 1.0 / (1.0 + dot(rimPos.xy  - p, rimPos.xy  - p) * 3.4);

    float shadow = 1.0;
    vec2 stepDir = normalize(keyPos.xy - p) * 0.05;
    for (int i = 1; i <= 3; i++) {
        vec2 samplePoint = p + stepDir * float(i);
        float height = posee_field(samplePoint, warp, s.warpAmount, s.t);
        float rayHeight = n + (keyPos.z - n * 0.35) * (float(i) / 4.0);
        shadow -= max(0.0, height - rayHeight) * 0.55;
    }
    shadow = clamp(shadow, 0.35, 1.0);

    float keyDiffuse  = max(0.0, dot(normal, keyDir))  * keyFall  * shadow;
    float fillDiffuse = max(0.0, dot(normal, fillDir)) * fillFall;
    float rimDiffuse  = max(0.0, dot(normal, rimDir))  * rimFall;

    vec3 albedo = mix(uColorA, uColorB, smoothstep(0.25, 0.75, n));
    albedo = mix(albedo, uColorC, smoothstep(0.55, 1.0, n) * (0.25 + uStrange * 0.55));

    vec3 color = albedo * uAmbient;
    color += albedo * uKeyLight * keyDiffuse * (1.30 + uAudioBass * 0.7);
    color += albedo * uColorC   * fillDiffuse * 0.55;
    color += uColorC * rimDiffuse * 0.22;

    return color;
}

float posee_height(vec2 p, PoseeScene s) {
    return posee_field(p, posee_warp(p, s), s.warpAmount, s.t);
}
`;

const FRAGMENT_SHADER = `#version 300 es
precision mediump float;

in vec2 vUv;
out vec4 fragColor;
${BACKDROP_UNIFORMS}
${BACKDROP_GLSL}

void main() {
    vec2 uv = vUv;
    // Correct for aspect so the noise cells stay round on a wide viewport. This is the space the
    // glass pass also works in, which is what lets a pane refract into the scene around it.
    PoseeScene s = posee_scene();
    vec2 p = uv * vec2(s.aspect, 1.0);

    vec3 color = posee_backdrop(p, s);

    // Vignette keeps the centre of the page readable; content sits on top of this.
    float vignette = smoothstep(1.25, 0.25, length(uv - 0.5) * 1.4);
    color *= 0.82 + vignette * 0.18;

    // Dithering. Without it, a slow wide gradient shows visible banding on 8-bit displays and
    // the whole effect reads as a compression artifact.
    float dither = (posee_hash(gl_FragCoord.xy) - 0.5) / 255.0;
    fragColor = vec4(clamp(color + dither, 0.0, 1.0), 1.0);
}`;

const instances = new Map();
let nextId = 1;

function parseColor(hex, fallback) {
    const match = /^#?([0-9a-f]{6})$/i.exec((hex ?? '').trim());
    if (!match) return fallback;
    const int = parseInt(match[1], 16);
    return [((int >> 16) & 255) / 255, ((int >> 8) & 255) / 255, (int & 255) / 255];
}

/** Per-frame approach toward a target colour, in place. No allocation in the draw loop. */
function easeColor(current, target) {
    current[0] += (target[0] - current[0]) * COLOR_EASE;
    current[1] += (target[1] - current[1]) * COLOR_EASE;
    current[2] += (target[2] - current[2]) * COLOR_EASE;
}

/**
 * Slower than the score ease (0.04) on purpose. The score is a number changing; this is the
 * whole page changing colour, and at the same rate it reads as a glitch rather than a mood.
 */
const COLOR_EASE = 0.018;

// ── The scene's base colours come from the design tokens ─────────────────────────────────
//
// They used to be three hardcoded dark purples, which was survivable only because this canvas
// was not actually visible: `.page` painted an opaque background straight over it. Now that the
// page stands aside for a running backdrop, the field IS the page's ground, and a ground that
// ignores the theme is a page whose text contrast is unmeasured.
//
// So the scene is built from --color-surface / --color-brand-surface / --color-brand, exactly
// like the CSS fallback on .fx-backdrop that it replaces. In light mode that is a pale lavender
// field; in dark mode the same three tokens have already flipped. Reading them rather than
// restating them is the same rule the Insights charts, the map pins and physics.js follow, and
// for the same reason: a literal freezes one theme into a page that renders both.

const SCENE_FALLBACK = {
    light: [[0.976, 0.973, 1.0], [0.929, 0.914, 1.0], [0.486, 0.227, 0.929]],
    dark: [[0.10, 0.03, 0.20], [0.16, 0.09, 0.30], [0.58, 0.36, 0.96]]
};

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

/**
 * The three base colours and the ambient floor for the current theme.
 *
 * The ambient is the part that is easy to get wrong. On a dark ground 0.62 leaves the lights
 * somewhere to go; on a light ground the same value renders a white-ish albedo as mid-grey, and
 * every text token in the app is measured against a near-white surface.
 */
function readSceneTokens() {
    const dark = prefersDark();
    const fallback = dark ? SCENE_FALLBACK.dark : SCENE_FALLBACK.light;

    try {
        const style = getComputedStyle(document.documentElement);
        const pick = (name, or) => parseColor(style.getPropertyValue(name).trim(), or);
        return {
            dark,
            ambient: dark ? 0.62 : 0.94,
            colors: [
                pick('--color-surface', fallback[0]),
                pick('--color-brand-surface', fallback[1]),
                pick('--color-brand', fallback[2])
            ]
        };
    } catch {
        return { dark, ambient: dark ? 0.62 : 0.94, colors: fallback };
    }
}

/**
 * Blends a comic's colour INTO the theme's own base rather than replacing it.
 *
 * The extracted colours are the ones a person would name looking at the comic: mid-lightness and
 * saturated. Correct for the artwork, wrong for something body text sits on. Replacing the base
 * with them would move the page's ground to an arbitrary lightness chosen by an image model, and
 * every text/surface pair in this app is measured against the tokens by ColorContrastTests.
 *
 * Mixing keeps the LIGHTNESS the theme's and takes the HUE from the comic — which is the half
 * that carries "this is that comic" anyway. The third slot takes more of the comic because it is
 * the highlight, not the ground.
 */
function blendToward(base, comic, amount) {
    return [
        base[0] + (comic[0] - base[0]) * amount,
        base[1] + (comic[1] - base[1]) * amount,
        base[2] + (comic[2] - base[2]) * amount
    ];
}

export function start(canvas, options = {}) {
    if (!canvas || !gfx.allows('full')) {
        return 0;
    }

    const surface = createSurface(canvas, { maxDpr: 1.25 });
    if (!surface) {
        return 0;
    }

    const { gl } = surface;

    let program;
    try {
        program = compileProgram(gl, FULLSCREEN_VERTEX_SHADER, FRAGMENT_SHADER);
    } catch (err) {
        // A driver that refuses this shader is not an app error — the CSS gradient stays.
        console.warn('[gradient] shader unavailable; falling back to CSS', err);
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
        ambient: gl.getUniformLocation(program, 'uAmbient')
    };

    // WebGL2 requires a bound VAO even when every vertex is generated from gl_VertexID.
    const vao = gl.createVertexArray();

    const scene = readSceneTokens();
    const id = nextId++;
    const instance = {
        gl,
        surface,
        program,
        vao,
        canvas,
        startTime: performance.now(),
        strange: Math.min(1, Math.max(0, (options.score ?? 40) / 100)),
        targetStrange: Math.min(1, Math.max(0, (options.score ?? 40) / 100)),
        colorA: [...scene.colors[0]],
        colorB: [...scene.colors[1]],
        colorC: [...scene.colors[2]],
        // Targets start equal to the live values, so a gradient nobody retints never eases.
        targetA: [...scene.colors[0]],
        targetB: [...scene.colors[1]],
        targetC: [...scene.colors[2]],
        // The theme's own base, kept so a palette can be cleared without the caller remembering
        // what it was — and re-read on a theme flip, which is why this is not a constant.
        baseA: [...scene.colors[0]],
        baseB: [...scene.colors[1]],
        baseC: [...scene.colors[2]],
        ambient: scene.ambient,
        /** The comic palette currently applied, so a theme flip can re-blend it. */
        palette: null,
        // Warm key against the cool violet field: the complementary split is what stops a
        // single-hue noise field from reading as flat.
        keyLight: parseColor(options.keyLight, [1.00, 0.86, 0.62]),
        audio: 0,
        audioBass: 0,
        stop: null
    };

    instance.stop = gfx.addTask(`gradient#${id}`, (now) => {
        if (!surface.beginFrame()) {
            stop(id);
            return;
        }

        // Ease toward the target so a score change is a transition, not a jump cut.
        instance.strange += (instance.targetStrange - instance.strange) * 0.04;

        // Colours ease toward their targets on the same terms the score does. A palette that
        // snapped would be a hard cut on a full-screen backdrop at the exact moment the user is
        // looking at the comic that caused it — the change has to read as the page taking on the
        // artwork's colour, which means it has to be slower than the eye.
        easeColor(instance.colorA, instance.targetA);
        easeColor(instance.colorB, instance.targetB);
        easeColor(instance.colorC, instance.targetC);

        gl.useProgram(program);
        gl.bindVertexArray(vao);
        gl.uniform1f(uniforms.time, (now - instance.startTime) / 1000);
        gl.uniform2f(uniforms.resolution, surface.width, surface.height);
        gl.uniform1f(uniforms.strange, instance.strange);
        gl.uniform3fv(uniforms.colorA, instance.colorA);
        gl.uniform3fv(uniforms.colorB, instance.colorB);
        gl.uniform3fv(uniforms.colorC, instance.colorC);
        gl.uniform3fv(uniforms.keyLight, instance.keyLight);
        gl.uniform1f(uniforms.audio, instance.audio);
        gl.uniform1f(uniforms.audioBass, instance.audioBass);
        gl.uniform1f(uniforms.ambient, instance.ambient);
        gl.drawArrays(gl.TRIANGLES, 0, 3);
        gl.bindVertexArray(null);

        surface.present();
    });

    instances.set(id, instance);
    notifyActive();
    return id;
}

export function setScore(id, score) {
    const instance = instances.get(id);
    if (instance) {
        instance.targetStrange = Math.min(1, Math.max(0, (score ?? 0) / 100));
    }
}

/**
 * Retints the backdrop to a comic's own colours.
 *
 * `palette` is the three hex strings the server sampled off the finished artwork (see
 * ComicPaletteExtractor). Passing nothing, or anything unparseable, restores the brand gradient
 * — which is what every route other than a comic should show, and what a comic drawn before the
 * extractor existed still ships.
 *
 * The two base colours are darkened and the third is kept bright as the highlight. That split is
 * the whole trick: the hue relationship is what makes the page recognisably this comic, while
 * the lightness stays where the contrast tokens assume it is, so body text over the backdrop is
 * as readable as it was with the brand purple.
 */
export function setPalette(id, palette) {
    const instance = instances.get(id);
    if (!instance) return false;

    const colors = Array.isArray(palette) ? palette : [];
    if (colors.length < 3) {
        instance.palette = null;
        applyPalette(instance);
        return false;
    }

    const parsed = colors.slice(0, 3).map(c => parseColor(c, null));
    if (parsed.some(c => c === null)) {
        return false;
    }

    instance.palette = parsed;
    applyPalette(instance);
    return true;
}

/**
 * Recomputes the targets from the theme base plus whatever comic palette is applied. Split out
 * because it is needed from two directions — a new comic, and a theme flip under an unchanged
 * comic — and doing it in only one of those is how a dark-mode toggle ends up showing light
 * mode's ground until the next navigation.
 */
function applyPalette(instance) {
    const p = instance.palette;
    if (!p) {
        instance.targetA = [...instance.baseA];
        instance.targetB = [...instance.baseB];
        instance.targetC = [...instance.baseC];
        return;
    }

    // The ground takes a little of the comic, the mid takes more, the highlight takes most. The
    // page's LIGHTNESS stays the theme's throughout; only the hue travels.
    instance.targetA = blendToward(instance.baseA, p[0], 0.18);
    instance.targetB = blendToward(instance.baseB, p[1], 0.42);
    instance.targetC = blendToward(instance.baseC, p[2], 0.75);
}

/**
 * Re-reads the tokens and re-blends. Bound to a theme change below rather than polled: the token
 * values change under the shader with no event of their own, and a backdrop still drawing light
 * mode's ground under a dark page is the most visible bug this file can have.
 */
function refreshTheme() {
    const scene = readSceneTokens();
    for (const instance of instances.values()) {
        instance.baseA = [...scene.colors[0]];
        instance.baseB = [...scene.colors[1]];
        instance.baseC = [...scene.colors[2]];
        instance.ambient = scene.ambient;
        applyPalette(instance);
    }
}

try {
    // Covers the OS-level flip. An explicit [data-theme] override is only set by ThemeUiTests
    // today; if a real control ever sets it, it should call refreshTheme itself rather than
    // this file growing a MutationObserver on the document element.
    window.matchMedia?.('(prefers-color-scheme: dark)').addEventListener('change', refreshTheme);
} catch {
    // Backdrop keeps the colours it started with.
}

export { refreshTheme };

/**
 * Feeds the audio analyser into the lighting. Called by the reactive driver rather than read
 * here, so a gradient with no sound playing does no analyser work at all.
 */
export function setAudioLevels(id, level, bass) {
    const instance = instances.get(id);
    if (instance) {
        // Smoothed on this side as well as in the analyser: the visual response to a transient
        // should decay slower than the transient does, or bright frames strobe.
        instance.audio += (level - instance.audio) * 0.25;
        instance.audioBass += (bass - instance.audioBass) * 0.25;
    }
}

/** Every live gradient id, so the audio driver can push levels without tracking handles. */
export function activeIds() {
    return [...instances.keys()];
}

// Listeners fired whenever the number of live gradients changes.
//
// Exposed as an event for the same reason setAudioLevels is a setter: the coupling points one
// way. This module must not learn that anything else depends on it being on screen — and
// something does. A glass pane refracts this scene by RECOMPUTING it, so a pane still running
// after the backdrop has gone is faithfully refracting a scene nobody can see. fx.js is where
// that pairing is enforced; here there is only a notification.
const activeListeners = new Set();

export function onActiveChanged(listener) {
    activeListeners.add(listener);
    return () => activeListeners.delete(listener);
}

function notifyActive() {
    // The handshake that lets `.page` stand aside. app.css keys the page's own opaque background
    // off the ABSENCE of this flag, so the ground is only ever handed over to a shader that is
    // genuinely drawing — every failure path (no WebGL2, reduced motion, a downgraded tier, a
    // lost context) leaves the CSS background exactly where it was.
    try {
        if (instances.size > 0) {
            document.documentElement.dataset.fxBackdrop = 'on';
        } else {
            delete document.documentElement.dataset.fxBackdrop;
        }
    } catch {
        // Without the flag the page keeps its own background. Nothing breaks; the shader is
        // simply covered, which is what happened for the whole life of this file until now.
    }

    for (const listener of activeListeners) {
        try {
            listener(instances.size);
        } catch {
            // A listener must not be able to take the backdrop down.
        }
    }
}

export function stop(id) {
    const instance = instances.get(id);
    if (!instance) return;

    instance.stop?.();
    instance.gl.deleteProgram(instance.program);
    instance.gl.deleteVertexArray(instance.vao);
    instance.surface.release();
    instances.delete(id);
    notifyActive();
}

// A downgrade out of 'full' has to tear these down, not just stop drawing: the point of the
// downgrade is to give the GPU back.
gfx.onTierChanged((tier) => {
    if (tier !== 'full') {
        for (const id of [...instances.keys()]) {
            stop(id);
        }
    }
});

window.poseeGradient = { start, stop, setScore, setPalette };
