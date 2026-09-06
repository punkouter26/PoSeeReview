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

const FRAGMENT_SHADER = `#version 300 es
precision mediump float;

in vec2 vUv;
out vec4 fragColor;

uniform float uTime;
uniform vec2  uResolution;
uniform float uStrange;    // 0..1
uniform vec3  uColorA;
uniform vec3  uColorB;
uniform vec3  uColorC;
uniform vec3  uKeyLight;   // rgb tint of the shadow-casting light
uniform float uAudio;      // 0..1 overall level
uniform float uAudioBass;  // 0..1 low band

// Cheap hash-based value noise. Gradient noise would look marginally better and cost roughly
// double; at this blur radius the difference is invisible.
float hash(vec2 p) {
    p = fract(p * vec2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return fract(p.x * p.y);
}

float valueNoise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    vec2 u = f * f * (3.0 - 2.0 * f);   // smoothstep weights

    float a = hash(i);
    float b = hash(i + vec2(1.0, 0.0));
    float c = hash(i + vec2(0.0, 1.0));
    float d = hash(i + vec2(1.0, 1.0));

    return mix(mix(a, b, u.x), mix(c, d, u.x), u.y);
}

// Three octaves. A fourth is not perceptible once the result is this desaturated.
float fbm(vec2 p) {
    float total = 0.0;
    float amplitude = 0.5;
    for (int i = 0; i < 3; i++) {
        total += valueNoise(p) * amplitude;
        p *= 2.0;
        amplitude *= 0.5;
    }
    return total;
}

/**
 * The scalar field everything is derived from — colour, normal, and shadow all read this, so
 * they cannot disagree about where a ridge is. The warp is passed in rather than recomputed:
 * it is the expensive part and it is identical for all four samples of a central difference.
 */
float field(vec2 p, vec2 warp, float warpAmount, float t) {
    return fbm(p * 2.1 + warp * warpAmount + t * 0.35);
}

void main() {
    vec2 uv = vUv;
    // Correct for aspect so the noise cells stay round on a wide viewport.
    vec2 p = uv * vec2(uResolution.x / max(uResolution.y, 1.0), 1.0);

    // Audio, when it is playing, pushes speed and warp. Silence leaves both at their score-driven
    // values, so this is additive detail rather than a dependency — the scene is complete with
    // the sound off, which is how it ships by default.
    float energy = uStrange + uAudio * 0.35;

    float speed = 0.02 + energy * 0.10;
    float t = uTime * speed;

    // Domain warping: sampling noise at a position that is itself offset by noise. This is what
    // turns smooth blobs into something that looks like it is churning.
    float warpAmount = 0.15 + energy * 0.85;
    vec2 warp = vec2(
        fbm(p * 1.4 + vec2(t, t * 0.7)),
        fbm(p * 1.4 + vec2(-t * 0.8, t * 1.1) + 5.2)
    );

    float n = field(p, warp, warpAmount, t);

    // ── Surface normal from the height field ────────────────────────────────────────────
    //
    // Central differences in the aspect-corrected space, at a fixed epsilon rather than a texel:
    // this field is smooth and low-frequency, so a per-pixel epsilon would sample inside a single
    // noise cell and return a normal dominated by interpolation error rather than by shape.
    float eps = 0.012;
    float hX = field(p + vec2(eps, 0.0), warp, warpAmount, t);
    float hY = field(p + vec2(0.0, eps), warp, warpAmount, t);

    // The z term is the relief scale. Smaller = more dramatic; 0.55 keeps it readable as an
    // undulation rather than a crumpled sheet.
    vec3 normal = normalize(vec3((n - hX) / eps, (n - hY) / eps, 0.55));

    // ── Three lights, orbiting ──────────────────────────────────────────────────────────
    //
    // Positions are in the same aspect-corrected space as the field, so a light stays where it
    // looks like it is on any viewport. They orbit at different rates and radii; matching rates
    // would make them read as one rigid rotating rig.
    float aspect = uResolution.x / max(uResolution.y, 1.0);
    vec2 centre = vec2(aspect, 1.0) * 0.5;

    vec3 keyPos  = vec3(centre + vec2(cos(uTime * 0.13), sin(uTime * 0.17)) * 0.42, 0.55);
    vec3 fillPos = vec3(centre + vec2(cos(-uTime * 0.09 + 2.1), sin(-uTime * 0.11 + 2.1)) * 0.55, 0.40);
    vec3 rimPos  = vec3(centre + vec2(cos(uTime * 0.21 + 4.2), sin(uTime * 0.19 + 4.2)) * 0.68, 0.28);

    vec3 surface = vec3(p, n * 0.35);

    vec3 keyDir  = normalize(keyPos  - surface);
    vec3 fillDir = normalize(fillPos - surface);
    vec3 rimDir  = normalize(rimPos  - surface);

    // Inverse-square-ish falloff, softened. True inverse square on a backdrop this close to the
    // lights produces a hotspot and a black surround, not a gradient.
    float keyFall  = 1.0 / (1.0 + dot(keyPos.xy  - p, keyPos.xy  - p) * 2.2);
    float fillFall = 1.0 / (1.0 + dot(fillPos.xy - p, fillPos.xy - p) * 2.6);
    float rimFall  = 1.0 / (1.0 + dot(rimPos.xy  - p, rimPos.xy  - p) * 3.4);

    // ── Soft shadow from the key light only ─────────────────────────────────────────────
    //
    // Three taps stepping toward the light. If the field is higher along the way than the
    // straight line from the surface to the light, something is between them. Fractional
    // occlusion across the taps is what makes the edge soft rather than stencil-hard.
    float shadow = 1.0;
    vec2 stepDir = normalize(keyPos.xy - p) * 0.05;
    for (int i = 1; i <= 3; i++) {
        vec2 samplePoint = p + stepDir * float(i);
        float height = field(samplePoint, warp, warpAmount, t);
        // Expected height of the ray at this distance, rising toward the light.
        float rayHeight = n + (keyPos.z - n * 0.35) * (float(i) / 4.0);
        shadow -= max(0.0, height - rayHeight) * 0.55;
    }
    shadow = clamp(shadow, 0.35, 1.0);

    float keyDiffuse  = max(0.0, dot(normal, keyDir))  * keyFall  * shadow;
    float fillDiffuse = max(0.0, dot(normal, fillDir)) * fillFall;
    float rimDiffuse  = max(0.0, dot(normal, rimDir))  * rimFall;

    // ── Colour ──────────────────────────────────────────────────────────────────────────
    vec3 albedo = mix(uColorA, uColorB, smoothstep(0.25, 0.75, n));
    albedo = mix(albedo, uColorC, smoothstep(0.55, 1.0, n) * (0.25 + uStrange * 0.55));

    // Ambient floor keeps the unlit side from going to black — content sits on top of this and
    // has to stay readable no matter where the lights happen to be.
    vec3 color = albedo * 0.62;
    color += albedo * uKeyLight * keyDiffuse * (1.30 + uAudioBass * 0.7);
    color += albedo * uColorC   * fillDiffuse * 0.55;
    color += uColorC * rimDiffuse * 0.22;   // Rim contributes light, not albedo: it is a highlight.

    // Vignette keeps the centre of the page readable; content sits on top of this.
    float vignette = smoothstep(1.25, 0.25, length(uv - 0.5) * 1.4);
    color *= 0.82 + vignette * 0.18;

    // Dithering. Without it, a slow wide gradient shows visible banding on 8-bit displays and
    // the whole effect reads as a compression artifact.
    float dither = (hash(gl_FragCoord.xy) - 0.5) / 255.0;
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
        audioBass: gl.getUniformLocation(program, 'uAudioBass')
    };

    // WebGL2 requires a bound VAO even when every vertex is generated from gl_VertexID.
    const vao = gl.createVertexArray();

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
        colorA: parseColor(options.colorA, [0.10, 0.03, 0.20]),
        colorB: parseColor(options.colorB, [0.29, 0.12, 0.58]),
        colorC: parseColor(options.colorC, [0.96, 0.34, 0.42]),
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
        gl.drawArrays(gl.TRIANGLES, 0, 3);
        gl.bindVertexArray(null);

        surface.present();
    });

    instances.set(id, instance);
    return id;
}

export function setScore(id, score) {
    const instance = instances.get(id);
    if (instance) {
        instance.targetStrange = Math.min(1, Math.max(0, (score ?? 0) / 100));
    }
}

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

export function stop(id) {
    const instance = instances.get(id);
    if (!instance) return;

    instance.stop?.();
    instance.gl.deleteProgram(instance.program);
    instance.gl.deleteVertexArray(instance.vao);
    instance.surface.release();
    instances.delete(id);
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

window.poseeGradient = { start, stop, setScore };
