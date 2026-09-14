// The backdrop scene, as a GLSL chunk shared by the two passes that need to agree about it.
//
// WHY THIS IS A SHARED STRING AND NOT COPY-PASTE.
//
// gradient.js draws this field full-screen. glass.js draws a refracted lookup INTO it, to make a
// pane that actually bends what is behind it. The second only works if it computes the identical
// field: a glass pane showing a slightly different backdrop from the one it is sitting on does
// not read as glass, it reads as a broken texture. Two copies of a shader this size would drift
// on the first tuning pass, and the failure would be subtle enough to survive review.
//
// WHY IT CAN BE RECOMPUTED RATHER THAN SAMPLED.
//
// The obvious way to build refractive glass is to read the pixels behind the pane and offset the
// lookup. You cannot do that here, and not for a reason that can be engineered around: the
// backdrop lives in a WebGL canvas, the page above it is DOM, and no browser exposes composited
// DOM to a shader. (This is the same wall comic-fx.js hits from the other side — the comic blob
// is cross-origin, so uploading it as a texture taints the canvas.)
//
// But this backdrop is PROCEDURAL: it is a pure function of position, time and a handful of
// uniforms. So the pane does not need to read what is behind it — it can evaluate it, at
// whatever coordinate the refraction asks for, including coordinates outside its own bounds.
// That is the whole trick, and it is why the glass costs a pass over the pane's area rather
// than a full-screen copy plus a blur.
//
// Cost note: `field` is the expensive call (three octaves through a domain warp). Everything
// that needs more than one sample of it — the normal, the shadow march, the dispersion — passes
// the warp in rather than recomputing it, because the warp is identical for every sample in a
// neighbourhood and is most of the cost.

export const BACKDROP_UNIFORMS = `
uniform float uTime;
uniform vec2  uResolution;
uniform float uStrange;    // 0..1
uniform vec3  uColorA;
uniform vec3  uColorB;
uniform vec3  uColorC;
uniform vec3  uKeyLight;   // rgb tint of the shadow-casting light
uniform float uAudio;      // 0..1 overall level
uniform float uAudioBass;  // 0..1 low band
// Unlit floor, as a fraction of albedo. A uniform rather than the constant it used to be,
// because the right value is opposite in the two themes: on a light ground the scene has to sit
// near full brightness or the page turns grey under body text tuned for a white surface, and on
// a dark one it has to sit low or the lights have nothing to be brighter than.
uniform float uAmbient;
`;

export const BACKDROP_GLSL = `
// Cheap hash-based value noise. Gradient noise would look marginally better and cost roughly
// double; at this blur radius the difference is invisible.
float posee_hash(vec2 p) {
    p = fract(p * vec2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return fract(p.x * p.y);
}

float posee_valueNoise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    vec2 u = f * f * (3.0 - 2.0 * f);   // smoothstep weights

    float a = posee_hash(i);
    float b = posee_hash(i + vec2(1.0, 0.0));
    float c = posee_hash(i + vec2(0.0, 1.0));
    float d = posee_hash(i + vec2(1.0, 1.0));

    return mix(mix(a, b, u.x), mix(c, d, u.x), u.y);
}

// Three octaves. A fourth is not perceptible once the result is this desaturated.
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

/**
 * The scalar field everything is derived from — colour, normal, and shadow all read this, so
 * they cannot disagree about where a ridge is. The warp is passed in rather than recomputed:
 * it is the expensive part and it is identical for all four samples of a central difference.
 */
float posee_field(vec2 p, vec2 warp, float warpAmount, float t) {
    return posee_fbm(p * 2.1 + warp * warpAmount + t * 0.35);
}

/** Everything the scene needs that does not vary per sample. Computed once per fragment. */
struct PoseeScene {
    float energy;
    float t;
    float warpAmount;
    float aspect;
    vec2  centre;
};

PoseeScene posee_scene() {
    PoseeScene s;
    // Audio, when it is playing, pushes speed and warp. Silence leaves both at their score-driven
    // values, so this is additive detail rather than a dependency — the scene is complete with
    // the sound off, which is how it ships by default.
    s.energy = uStrange + uAudio * 0.35;
    s.t = uTime * (0.02 + s.energy * 0.10);
    // Domain warping: sampling noise at a position that is itself offset by noise. This is what
    // turns smooth blobs into something that looks like it is churning.
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

/**
 * The lit backdrop colour at an aspect-corrected position.
 *
 * 'p' is in the SAME space for both passes — x scaled by aspect, y in 0..1 — which is what lets
 * the glass ask for a coordinate slightly off its own edge and get back exactly the pixel the
 * full-screen pass would have drawn there.
 */
vec3 posee_backdrop(vec2 p, PoseeScene s) {
    vec2 warp = posee_warp(p, s);
    float n = posee_field(p, warp, s.warpAmount, s.t);

    // ── Surface normal from the height field ────────────────────────────────────────────
    //
    // Central differences in the aspect-corrected space, at a fixed epsilon rather than a texel:
    // this field is smooth and low-frequency, so a per-pixel epsilon would sample inside a single
    // noise cell and return a normal dominated by interpolation error rather than by shape.
    float eps = 0.012;
    float hX = posee_field(p + vec2(eps, 0.0), warp, s.warpAmount, s.t);
    float hY = posee_field(p + vec2(0.0, eps), warp, s.warpAmount, s.t);

    // The z term is the relief scale. Smaller = more dramatic; 0.55 keeps it readable as an
    // undulation rather than a crumpled sheet.
    vec3 normal = normalize(vec3((n - hX) / eps, (n - hY) / eps, 0.55));

    // ── Three lights, orbiting ──────────────────────────────────────────────────────────
    //
    // They orbit at different rates and radii; matching rates would make them read as one rigid
    // rotating rig.
    vec3 keyPos  = vec3(s.centre + vec2(cos(uTime * 0.13), sin(uTime * 0.17)) * 0.42, 0.55);
    vec3 fillPos = vec3(s.centre + vec2(cos(-uTime * 0.09 + 2.1), sin(-uTime * 0.11 + 2.1)) * 0.55, 0.40);
    vec3 rimPos  = vec3(s.centre + vec2(cos(uTime * 0.21 + 4.2), sin(uTime * 0.19 + 4.2)) * 0.68, 0.28);

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
    // Three taps stepping toward the light. Fractional occlusion across the taps is what makes
    // the edge soft rather than stencil-hard.
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

    // ── Colour ──────────────────────────────────────────────────────────────────────────
    vec3 albedo = mix(uColorA, uColorB, smoothstep(0.25, 0.75, n));
    albedo = mix(albedo, uColorC, smoothstep(0.55, 1.0, n) * (0.25 + uStrange * 0.55));

    // Ambient floor keeps the unlit side from going to black — content sits on top of this and
    // has to stay readable no matter where the lights happen to be.
    vec3 color = albedo * uAmbient;
    color += albedo * uKeyLight * keyDiffuse * (1.30 + uAudioBass * 0.7);
    color += albedo * uColorC   * fillDiffuse * 0.55;
    color += uColorC * rimDiffuse * 0.22;   // Rim contributes light, not albedo: it is a highlight.

    return color;
}

/**
 * Just the height at a position. The glass needs this for its own lighting without paying for
 * the three-light solve, and it is the one place a bare field read is the right call.
 */
float posee_height(vec2 p, PoseeScene s) {
    return posee_field(p, posee_warp(p, s), s.warpAmount, s.t);
}
`;
