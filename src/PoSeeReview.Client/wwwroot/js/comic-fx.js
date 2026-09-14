// Print-press post-process for the comic panel: halftone dots, paper grain, lens aberration,
// lit paper fibre, a bloom on the bright inks, and a vignette.
//
// THE IMPORTANT PART IS WHAT THIS DOES NOT DO.
//
// The comic PNG is the single most shareable thing the app produces. Replacing its <img> with a
// <canvas> would silently break long-press-save on mobile and right-click-save-image on desktop
// — the two ways people actually keep an image. So the original <img> stays in the DOM and stays
// the accessible, saveable element; the canvas is layered over it purely as decoration, marked
// aria-hidden and pointer-events:none. If anything here fails, the untouched <img> is already
// what the user sees.
//
// This also means the effect is never applied to a cross-origin blob URL without CORS: reading
// pixels from one taints the canvas, and while we never call readPixels, texImage2D on a tainted
// source throws a SecurityError. We ask for crossOrigin and fall back silently if it is refused.
//
// PASS STRUCTURE. Five passes, and the ordering is not arbitrary:
//
//   1. base       — aberration, halftone, grain, paper lighting, at full resolution.
//   2. bright     — luminance threshold, at HALF resolution.
//   3. blur H  ┐  — separable Gaussian on the half-res buffer. Separable because a 9-tap 2D
//   4. blur V  ┘    kernel is 81 samples per pixel and two 9-tap 1D passes are 18.
//   5. composite  — base + bloom + vignette, back to full resolution.
//
// Bloom runs at half resolution on purpose. A bloom is a low-frequency signal by definition;
// computing it at full resolution costs 4x the fill rate to produce a result that is then
// blurred until the extra detail is gone. The bright pass IS the downsample — one bilinear fetch
// per output pixel, so it costs nothing beyond the pass itself.

import {
    gfx, createSurface, compileProgram, FULLSCREEN_VERTEX_SHADER
} from './gfx-core.js';

// How fast the ink development chases the target the stream last reported, as an exponential
// rate per second. Roughly a third of a second to close most of a gap: fast enough that the last
// phase does not leave the strip visibly half-drawn, slow enough that a burst of buffered phase
// events reads as developing rather than as a jump cut.
const REVEAL_GLIDE_RATE = 3.4;

// After this the shockwave's envelope is below a thousandth and the uniform goes back to its
// inactive sentinel. Kept as a constant because it has to agree with the exp() decay in the
// shader — shortening one without the other either clips the wave or leaves a dead branch hot.
const RIPPLE_LIFETIME_SECONDS = 2.2;

// Shared preamble. Duplicated helper functions across five shaders is how they drift apart.
const COMMON = `
float hash(vec2 p) {
    p = fract(p * vec2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return fract(p.x * p.y);
}

float valueNoise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    vec2 u = f * f * (3.0 - 2.0 * f);
    float a = hash(i);
    float b = hash(i + vec2(1.0, 0.0));
    float c = hash(i + vec2(0.0, 1.0));
    float d = hash(i + vec2(1.0, 1.0));
    return mix(mix(a, b, u.x), mix(c, d, u.x), u.y);
}

float luma(vec3 c) {
    return dot(c, vec3(0.2126, 0.7152, 0.0722));
}`;

const BASE_SHADER = `#version 300 es
precision highp float;

in vec2 vUv;
out vec4 fragColor;

uniform sampler2D uImage;
uniform vec2  uResolution;
uniform float uTime;
uniform float uHalftone;
uniform float uGrain;
uniform float uAberration;
uniform float uLighting;
uniform vec2  uLightDir;
${COMMON}

/**
 * Height of the paper surface at a point. Two octaves of value noise: a coarse one for the
 * cockle of a sheet, a fine one for the fibre. This is what the normal is derived from.
 */
float paperHeight(vec2 p) {
    return valueNoise(p * 42.0) * 0.65 + valueNoise(p * 180.0) * 0.35;
}

void main() {
    // Flip V: WebGL samples bottom-up, the image decodes top-down.
    vec2 uv = vec2(vUv.x, 1.0 - vUv.y);

    vec2 centered = uv - 0.5;
    float edge = dot(centered, centered);

    // Chromatic aberration scaled by distance from centre, the way a real lens behaves. Uniform
    // aberration across the frame just looks like a misregistered print.
    vec2 shift = centered * edge * uAberration * 0.06;
    vec3 color = vec3(
        texture(uImage, uv - shift).r,
        texture(uImage, uv).g,
        texture(uImage, uv + shift).b
    );

    // Halftone. The dot grid is computed in device pixels so the dots stay a constant physical
    // size regardless of how large the panel is laid out.
    if (uHalftone > 0.001) {
        float scale = 3.4;
        // 15 degrees is the classic screen angle for black plates; axis-aligned dots moire
        // badly against the panel borders the image generator tends to draw.
        float a = radians(15.0);
        mat2 rot = mat2(cos(a), -sin(a), sin(a), cos(a));
        vec2 grid = rot * (uv * uResolution / scale);

        vec2 cell = fract(grid) - 0.5;
        float dist = length(cell);

        // Dot radius tracks inverse luminance: dark areas grow their dots, exactly like ink
        // coverage on paper.
        float radius = sqrt(max(0.0, 1.0 - luma(color))) * 0.62;
        float dotMask = smoothstep(radius, radius - 0.16, dist);

        vec3 inked = mix(vec3(1.0), color * 0.86, dotMask);
        color = mix(color, inked, uHalftone * 0.55);
    }

    // ── Dynamic lighting on the paper surface ───────────────────────────────────────────
    //
    // The panel is a flat quad, so there is no geometry to light. What there IS is a height
    // field — the paper itself. Deriving a normal from it by central differences and running one
    // Blinn-Phong term gives a sheet that catches a moving light along its fibre, which is the
    // difference between "an image on a screen" and "a printed thing under a lamp".
    //
    // Central differences over a screen-space texel, not a fixed epsilon: the fibre has to stay
    // the same physical size whether the panel is 320px wide or 900px.
    if (uLighting > 0.001) {
        vec2 texel = 1.0 / max(uResolution, vec2(1.0));
        float hL = paperHeight(uv - vec2(texel.x, 0.0));
        float hR = paperHeight(uv + vec2(texel.x, 0.0));
        float hD = paperHeight(uv - vec2(0.0, texel.y));
        float hU = paperHeight(uv + vec2(0.0, texel.y));

        // The 0.04 z term sets how pronounced the relief is. Larger looks like hammered metal.
        vec3 normal = normalize(vec3(hL - hR, hD - hU, 0.04));
        vec3 lightDir = normalize(vec3(uLightDir, 0.85));

        float diffuse = max(0.0, dot(normal, lightDir));
        vec3 viewDir = vec3(0.0, 0.0, 1.0);
        vec3 halfway = normalize(lightDir + viewDir);
        // Tight, weak specular. Paper is not glossy; this is the sheen off a raised fibre, and
        // pushing it any harder reads as plastic laminate.
        float specular = pow(max(0.0, dot(normal, halfway)), 48.0) * 0.16;

        color *= mix(1.0, 0.86 + diffuse * 0.30, uLighting);
        color += specular * uLighting;
    }

    // Paper grain. Animated very slightly so a still image does not look like a dead texture,
    // but slowly enough that it never reads as video noise.
    float grain = hash(uv * uResolution + floor(uTime * 8.0)) - 0.5;
    color += grain * uGrain * 0.12;

    // Warm the whites a little: pure #FFF reads as screen, not paper.
    color = mix(color, color * vec3(1.02, 1.0, 0.96), 0.5);

    fragColor = vec4(max(color, vec3(0.0)), 1.0);
}`;

const BRIGHT_SHADER = `#version 300 es
precision mediump float;

in vec2 vUv;
out vec4 fragColor;

uniform sampler2D uScene;
uniform float uThreshold;
${COMMON}

void main() {
    vec3 color = texture(uScene, vUv).rgb;
    float brightness = luma(color);

    // Soft knee rather than a hard step. A hard threshold makes the bloom pop on and off as an
    // area crosses it, which on an animated grain field flickers constantly.
    float contribution = smoothstep(uThreshold, uThreshold + 0.22, brightness);
    fragColor = vec4(color * contribution, 1.0);
}`;

const BLUR_SHADER = `#version 300 es
precision mediump float;

in vec2 vUv;
out vec4 fragColor;

uniform sampler2D uSource;
uniform vec2 uDirection;   // (texelX, 0) horizontal, (0, texelY) vertical

// Nine-tap Gaussian collapsed to five bilinear fetches. Sampling between texel centres lets one
// hardware fetch return a weighted pair, which is the standard trick and halves the bandwidth.
const float OFFSETS[3] = float[3](0.0, 1.3846153846, 3.2307692308);
const float WEIGHTS[3] = float[3](0.2270270270, 0.3162162162, 0.0702702703);

void main() {
    vec3 result = texture(uSource, vUv).rgb * WEIGHTS[0];
    for (int i = 1; i < 3; i++) {
        vec2 offset = uDirection * OFFSETS[i];
        result += texture(uSource, vUv + offset).rgb * WEIGHTS[i];
        result += texture(uSource, vUv - offset).rgb * WEIGHTS[i];
    }
    fragColor = vec4(result, 1.0);
}`;

// Composite, and the two moments layered into it.
//
// A separate pass for either of these would mean another full-resolution fetch of the scene
// buffer for effects that are only live for about a second each. Both need the scene texture and
// both write the final pixel, so both belong here — the ripple as a displaced lookup, the reveal
// as a mask over the result.
const COMPOSITE_SHADER = `#version 300 es
precision highp float;

in vec2 vUv;
out vec4 fragColor;

uniform sampler2D uScene;
uniform sampler2D uBloom;
uniform float uBloomStrength;
uniform float uAspect;

// Score shockwave: xy = origin in UV, z = age in seconds. z < 0 means inactive, which is the
// resting state — this uniform is set once at the reveal and otherwise costs one branch.
uniform vec3  uRipple;
uniform float uRippleStrength;

// Ink development. uReveal runs 0..1 across the whole strip; uRevealBands is how many stacked
// panels to develop in sequence. uReveal >= 1.0 switches the whole thing off.
uniform float uReveal;
uniform float uRevealBands;
uniform vec3  uPaper;
${COMMON}

void main() {
    vec2 uv = vUv;

    // ── Shockwave ───────────────────────────────────────────────────────────────────────
    //
    // An expanding annulus that displaces the lookup outward along its own radius and splits the
    // channels across the wavefront. Aspect-corrected, or the ring is an ellipse on a wide panel
    // and reads as a scanline artefact rather than as a wave.
    float ripple = 0.0;
    vec2 rippleDir = vec2(0.0);

    if (uRipple.z >= 0.0) {
        vec2 delta = (uv - uRipple.xy) * vec2(uAspect, 1.0);
        float dist = length(delta);
        rippleDir = dist > 0.0001 ? delta / dist : vec2(0.0);

        // Expands fast and decays faster. A slow ring reads as a ripple in water; the score
        // landing is an impact, and an impact is over before you can follow it.
        float radius = uRipple.z * 1.45;
        float width = 0.10 + uRipple.z * 0.05;
        float band = smoothstep(radius - width, radius, dist)
                   * (1.0 - smoothstep(radius, radius + width, dist));

        ripple = band * exp(-uRipple.z * 2.9) * uRippleStrength;
        uv += rippleDir * ripple * 0.045 / vec2(uAspect, 1.0);
    }

    vec3 scene = texture(uScene, uv).rgb;

    // Chromatic split only at the wavefront, and only while there is one. Applying it everywhere
    // would double the aberration the base pass already applies across the whole frame.
    if (ripple > 0.002) {
        vec2 offset = rippleDir * ripple * 0.03 / vec2(uAspect, 1.0);
        scene.r = texture(uScene, uv + offset).r;
        scene.b = texture(uScene, uv - offset).b;
    }

    vec3 bloom = texture(uBloom, uv).rgb;

    // Screen blend, not additive. Additive bloom drives already-bright ink past 1.0 and clips it
    // to flat white, destroying the linework the bloom was meant to flatter.
    vec3 color = 1.0 - (1.0 - scene) * (1.0 - bloom * uBloomStrength);

    // The wavefront itself carries a little light, so the ring is visible on dark ink as well as
    // on paper. Without it the shockwave disappears wherever the comic happens to be black.
    color += ripple * 0.30;

    vec2 centered = vUv - 0.5;
    float edgeSq = dot(centered, centered);
    float vignette = smoothstep(0.95, 0.15, edgeSq * 1.6);
    color *= 0.90 + vignette * 0.10;

    // ── Ink development ─────────────────────────────────────────────────────────────────
    //
    // The strip is masked back to bare paper and developed into view band by band, top down, in
    // step with the pipeline phases the server is streaming. The mask is a noise threshold rather
    // than a hard wipe: ink spreading into paper starts from many points at once and joins up,
    // and a straight edge travelling down the page reads as a progress bar.
    //
    // The image's top is at vUv.y = 1 here — the base pass flipped V when it sampled the source,
    // so the scene texture is stored inverted relative to the image.
    if (uReveal < 1.0) {
        float fromTop = 1.0 - vUv.y;
        float bands = max(1.0, uRevealBands);
        float band = floor(min(fromTop * bands, bands - 1.0));

        // The overlap term is why a two-panel strip develops continuously instead of finishing
        // one panel, pausing, and starting the next.
        float local = clamp(uReveal * (bands + 0.4) - band, 0.0, 1.0);

        // Two octaves: the coarse one makes ink arrive in blotches, the fine one gives the
        // boundary a ragged fibre edge instead of a clean contour.
        float seeds = valueNoise(vec2(vUv.x * uAspect, vUv.y) * 11.0) * 0.68
                    + valueNoise(vUv * 47.0) * 0.32;

        float developed = smoothstep(seeds - 0.20, seeds + 0.20, local * 1.4 - 0.2);

        // Wet frontier: ink is darkest where it has just arrived and is still spreading.
        float wet = 1.0 - abs(developed - 0.5) * 2.0;

        color = mix(uPaper, color, developed);
        color *= 1.0 - wet * 0.26 * step(0.02, developed);
    }

    // Opaque on purpose. During the reveal this canvas is what hides the finished <img> beneath
    // it, and a transparent unrevealed area would show the whole comic through the mask.
    fragColor = vec4(clamp(color, 0.0, 1.0), 1.0);
}`;

const instances = new Map();
let nextId = 1;

/**
 * The paper the strip develops onto, read from the design tokens for the reason the Insights
 * charts and the map pins already read theirs: a literal hex freezes one theme into a page that
 * renders both. Falls back to a warm off-white rather than pure white — #FFF reads as screen,
 * and the base pass already warms its own whites for the same reason.
 */
function readPaperColor() {
    const fallback = [0.976, 0.969, 0.949];
    try {
        const value = getComputedStyle(document.documentElement)
            .getPropertyValue('--color-surface-alt').trim()
            || getComputedStyle(document.documentElement).getPropertyValue('--color-card').trim();
        const match = /^#?([0-9a-f]{6})$/i.exec(value);
        if (!match) return fallback;
        const int = parseInt(match[1], 16);
        return [((int >> 16) & 255) / 255, ((int >> 8) & 255) / 255, (int & 255) / 255];
    } catch {
        return fallback;
    }
}

/** A colour-only render target. Linear filtering: the blur and the upsample both depend on it. */
function makeTarget(gl, width, height) {
    const texture = gl.createTexture();
    gl.bindTexture(gl.TEXTURE_2D, texture);
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA8, width, height, 0, gl.RGBA, gl.UNSIGNED_BYTE, null);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);

    const fbo = gl.createFramebuffer();
    gl.bindFramebuffer(gl.FRAMEBUFFER, fbo);
    gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D, texture, 0);
    const ok = gl.checkFramebufferStatus(gl.FRAMEBUFFER) === gl.FRAMEBUFFER_COMPLETE;
    gl.bindFramebuffer(gl.FRAMEBUFFER, null);
    gl.bindTexture(gl.TEXTURE_2D, null);

    return ok ? { texture, fbo, width, height } : null;
}

function freeTarget(gl, target) {
    if (!target) return;
    gl.deleteTexture(target.texture);
    gl.deleteFramebuffer(target.fbo);
}

/** Rebuilds every intermediate buffer for a new panel size. */
function resizeTargets(instance, width, height) {
    const { gl } = instance;
    const halfWidth = Math.max(1, width >> 1);
    const halfHeight = Math.max(1, height >> 1);

    for (const target of [instance.scene, instance.bloomA, instance.bloomB]) {
        freeTarget(gl, target);
    }

    instance.scene = makeTarget(gl, width, height);
    instance.bloomA = makeTarget(gl, halfWidth, halfHeight);
    instance.bloomB = makeTarget(gl, halfWidth, halfHeight);
    instance.targetWidth = width;
    instance.targetHeight = height;

    return !!(instance.scene && instance.bloomA && instance.bloomB);
}

function uniformsOf(gl, program, names) {
    const out = {};
    for (const name of names) {
        out[name] = gl.getUniformLocation(program, name);
    }
    return out;
}

/** Binds a target (or the surface) and draws the fullscreen triangle. */
function pass(instance, program, target) {
    const { gl, surface } = instance;
    if (target) {
        gl.bindFramebuffer(gl.FRAMEBUFFER, target.fbo);
        gl.viewport(0, 0, target.width, target.height);
    } else {
        surface.bindTarget();
    }
    gl.useProgram(program);
    gl.bindVertexArray(instance.vao);
    gl.drawArrays(gl.TRIANGLES, 0, 3);
}

export function attach(canvas, image, options = {}) {
    if (!canvas || !image || !gfx.allows('full')) {
        return 0;
    }

    // naturalWidth is 0 until decode finishes; uploading then produces a black panel.
    if (!image.complete || image.naturalWidth === 0) {
        return 0;
    }

    const surface = createSurface(canvas, { maxDpr: 2 });
    if (!surface) return 0;

    const { gl } = surface;

    let programs;
    try {
        programs = {
            base: compileProgram(gl, FULLSCREEN_VERTEX_SHADER, BASE_SHADER),
            bright: compileProgram(gl, FULLSCREEN_VERTEX_SHADER, BRIGHT_SHADER),
            blur: compileProgram(gl, FULLSCREEN_VERTEX_SHADER, BLUR_SHADER),
            composite: compileProgram(gl, FULLSCREEN_VERTEX_SHADER, COMPOSITE_SHADER)
        };
    } catch (err) {
        console.warn('[comic-fx] shader unavailable; leaving the plain image', err);
        surface.release();
        return 0;
    }

    const texture = gl.createTexture();
    gl.bindTexture(gl.TEXTURE_2D, texture);
    // CLAMP_TO_EDGE matters: the aberration pass samples slightly outside the image, and REPEAT
    // would wrap the opposite edge into the frame as a coloured fringe.
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);

    try {
        gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, gl.RGBA, gl.UNSIGNED_BYTE, image);
    } catch (err) {
        // Tainted canvas: the blob SAS URL was served without CORS headers. The <img> underneath
        // is unaffected, so the user still sees their comic.
        console.warn('[comic-fx] image is not readable by WebGL (CORS); leaving the plain image', err);
        gl.deleteTexture(texture);
        for (const program of Object.values(programs)) gl.deleteProgram(program);
        surface.release();
        return 0;
    }

    const id = nextId++;
    const instance = {
        gl,
        surface,
        canvas,
        programs,
        texture,
        vao: gl.createVertexArray(),
        scene: null,
        bloomA: null,
        bloomB: null,
        targetWidth: 0,
        targetHeight: 0,
        startTime: performance.now(),
        halftone: options.halftone ?? 1,
        grain: options.grain ?? 1,
        aberration: options.aberration ?? 1,
        lighting: options.lighting ?? 1,
        bloom: options.bloom ?? 0.55,
        threshold: options.threshold ?? 0.62,
        stop: null,

        // ── Ink development ─────────────────────────────────────────────────────────────
        // `reveal` is what the shader sees; `revealTarget` is where the stream says it should
        // be. The gap between them is eased per frame, so a phase event that lands late (or a
        // whole run of them arriving at once because App Service buffered the stream) glides
        // instead of snapping. 1 means "no reveal", which is the resting state.
        reveal: options.reveal ?? 1,
        revealTarget: options.reveal ?? 1,
        // Two, because the server caps the pipeline at two panels and stacks them vertically
        // (ComicTextOverlayService.GetPanelBounds). It is a parameter rather than a constant so
        // that if PanelCount ever reaches ComicDto, the caller can pass the real number without
        // this file changing.
        revealBands: options.revealBands ?? 2,
        paper: readPaperColor(),

        // ── Shockwave ───────────────────────────────────────────────────────────────────
        // A negative age is the inactive sentinel; the shader branches on it.
        rippleStartedAt: -1,
        rippleOrigin: [0.5, 0.5],
        rippleStrength: 0,

        uniforms: {
            base: uniformsOf(gl, programs.base, [
                'uImage', 'uResolution', 'uTime', 'uHalftone', 'uGrain',
                'uAberration', 'uLighting', 'uLightDir'
            ]),
            bright: uniformsOf(gl, programs.bright, ['uScene', 'uThreshold']),
            blur: uniformsOf(gl, programs.blur, ['uSource', 'uDirection']),
            composite: uniformsOf(gl, programs.composite, [
                'uScene', 'uBloom', 'uBloomStrength', 'uAspect',
                'uRipple', 'uRippleStrength', 'uReveal', 'uRevealBands', 'uPaper'
            ])
        }
    };

    instance.stop = gfx.addTask(`comic-fx#${id}`, (now, elapsed) => {
        if (!surface.beginFrame()) {
            detach(id);
            return;
        }

        const width = surface.width;
        const height = surface.height;
        if (width !== instance.targetWidth || height !== instance.targetHeight) {
            if (!resizeTargets(instance, width, height)) {
                // A driver that will not give us intermediate buffers cannot run this chain.
                // The <img> is still there, so stopping is a complete and correct outcome.
                console.warn('[comic-fx] render targets unavailable; leaving the plain image');
                detach(id);
                return;
            }
        }

        const seconds = (now - instance.startTime) / 1000;
        const u = instance.uniforms;

        // ── Pass 1: base ────────────────────────────────────────────────────────────────
        gl.useProgram(programs.base);
        gl.activeTexture(gl.TEXTURE0);
        gl.bindTexture(gl.TEXTURE_2D, instance.texture);
        gl.uniform1i(u.base.uImage, 0);
        gl.uniform2f(u.base.uResolution, width, height);
        gl.uniform1f(u.base.uTime, seconds);
        gl.uniform1f(u.base.uHalftone, instance.halftone);
        gl.uniform1f(u.base.uGrain, instance.grain);
        gl.uniform1f(u.base.uAberration, instance.aberration);
        gl.uniform1f(u.base.uLighting, instance.lighting);
        // The light orbits slowly. A static light is indistinguishable from a baked texture;
        // one full revolution every ~26 seconds is under the threshold at which motion draws
        // the eye away from the comic, which is the thing the user is actually here to read.
        gl.uniform2f(u.base.uLightDir, Math.cos(seconds * 0.24) * 0.6, Math.sin(seconds * 0.24) * 0.6);
        pass(instance, programs.base, instance.scene);

        // ── Pass 2: bright extract, at half resolution ──────────────────────────────────
        gl.useProgram(programs.bright);
        gl.activeTexture(gl.TEXTURE0);
        gl.bindTexture(gl.TEXTURE_2D, instance.scene.texture);
        gl.uniform1i(u.bright.uScene, 0);
        gl.uniform1f(u.bright.uThreshold, instance.threshold);
        pass(instance, programs.bright, instance.bloomA);

        // ── Passes 3 and 4: separable blur ──────────────────────────────────────────────
        gl.useProgram(programs.blur);
        gl.activeTexture(gl.TEXTURE0);
        gl.bindTexture(gl.TEXTURE_2D, instance.bloomA.texture);
        gl.uniform1i(u.blur.uSource, 0);
        gl.uniform2f(u.blur.uDirection, 1 / instance.bloomA.width, 0);
        pass(instance, programs.blur, instance.bloomB);

        gl.bindTexture(gl.TEXTURE_2D, instance.bloomB.texture);
        gl.uniform2f(u.blur.uDirection, 0, 1 / instance.bloomB.height);
        pass(instance, programs.blur, instance.bloomA);

        // ── Pass 5: composite back to the surface ───────────────────────────────────────
        gl.useProgram(programs.composite);
        gl.activeTexture(gl.TEXTURE0);
        gl.bindTexture(gl.TEXTURE_2D, instance.scene.texture);
        gl.uniform1i(u.composite.uScene, 0);
        gl.activeTexture(gl.TEXTURE1);
        gl.bindTexture(gl.TEXTURE_2D, instance.bloomA.texture);
        gl.uniform1i(u.composite.uBloom, 1);
        gl.uniform1f(u.composite.uBloomStrength, instance.bloom);
        gl.uniform1f(u.composite.uAspect, width / Math.max(1, height));

        // Ease the reveal toward whatever the stream last reported. Frame-rate independent, so a
        // 30fps phone develops the strip at the same speed as a 120Hz tablet — a per-frame lerp
        // would make the reveal four times slower on the device least able to hide it.
        if (instance.reveal < 1 || instance.revealTarget < 1) {
            const dt = Math.min(0.1, Math.max(0.001, (elapsed || 16.7) / 1000));
            const k = 1 - Math.exp(-REVEAL_GLIDE_RATE * dt);
            instance.reveal += (instance.revealTarget - instance.reveal) * k;
            // Snap the last sliver. An exponential glide never arrives, and a reveal stuck at
            // 0.998 leaves a permanent haze of undeveloped paper over the finished comic.
            if (instance.revealTarget >= 1 && instance.reveal > 0.995) {
                instance.reveal = 1;
            }
        }
        gl.uniform1f(u.composite.uReveal, instance.reveal);
        gl.uniform1f(u.composite.uRevealBands, instance.revealBands);
        gl.uniform3fv(u.composite.uPaper, instance.paper);

        const rippleAge = instance.rippleStartedAt >= 0
            ? (now - instance.rippleStartedAt) / 1000
            : -1;
        // Retire the wave once its envelope has decayed past visibility, so the branch in the
        // shader goes cold again rather than evaluating an annulus off the edge of the frame
        // forever.
        if (rippleAge > RIPPLE_LIFETIME_SECONDS) {
            instance.rippleStartedAt = -1;
        }
        gl.uniform3f(u.composite.uRipple,
            instance.rippleOrigin[0], instance.rippleOrigin[1],
            instance.rippleStartedAt >= 0 ? rippleAge : -1);
        gl.uniform1f(u.composite.uRippleStrength, instance.rippleStrength);

        pass(instance, programs.composite, null);

        // Leave unit 0 active: every other effect assumes it, and a stale TEXTURE1 binding on a
        // shared pooled context is the kind of state leak that shows up as someone else's bug.
        gl.activeTexture(gl.TEXTURE0);
        gl.bindVertexArray(null);

        surface.present();
    });

    instances.set(id, instance);
    canvas.dataset.comicFx = 'on';
    return id;
}

export function detach(id) {
    const instance = instances.get(id);
    if (!instance) return;

    instance.stop?.();
    const { gl } = instance;
    gl.deleteTexture(instance.texture);
    for (const program of Object.values(instance.programs)) {
        gl.deleteProgram(program);
    }
    gl.deleteVertexArray(instance.vao);
    for (const target of [instance.scene, instance.bloomA, instance.bloomB]) {
        freeTarget(gl, target);
    }
    instance.surface.release();

    if (instance.canvas) {
        delete instance.canvas.dataset.comicFx;
    }
    instances.delete(id);
}

/**
 * Sets how far the strip has developed, 0 (bare paper) to 1 (finished, effect off).
 *
 * The caller drives this from the pipeline phases the server streams, so the wait is spent
 * watching the comic actually appear rather than watching a stepper describe it. The value is a
 * TARGET — the render loop eases toward it — which matters because the SSE stream can be buffered
 * by App Service and deliver several phases in one burst.
 *
 * Returns false when there is no instance, which is the common case: comic-fx only attaches at
 * the `full` tier and only when the blob is CORS-readable. The caller must not depend on this
 * having happened, and the CSS reveal in comic-reveal.js is what covers everyone else.
 */
export function setReveal(id, progress) {
    const instance = instances.get(id);
    if (!instance) return false;
    instance.revealTarget = Math.min(1, Math.max(0, progress));
    return true;
}

/**
 * Fires the score shockwave from a point on the panel, given in 0..1 UV.
 *
 * The reveal is the payoff of a ten-second wait, and until now the comic itself did not react to
 * its own score at all — the ring counted, a chord played, ink sprayed over the top, and the
 * artwork sat there. One expanding ring of displacement ties the three together.
 *
 * @param {number} id      handle from attach()
 * @param {number} score   0-100; scales how hard the wave hits
 * @param {number} originX 0..1 across the panel
 * @param {number} originY 0..1 down the panel
 */
export function shockwave(id, score = 50, originX = 0.5, originY = 0.5) {
    const instance = instances.get(id);
    if (!instance) return false;

    const strange = Math.min(1, Math.max(0, score / 100));
    instance.rippleStartedAt = performance.now();
    instance.rippleOrigin = [
        Math.min(1, Math.max(0, originX)),
        // The origin arrives in image coordinates (0 at the top) and the scene texture is stored
        // flipped, the same conversion the reveal mask makes.
        1 - Math.min(1, Math.max(0, originY))
    ];
    // Never zero even at score 0: a comic that scored nothing still landed, and a silent ring is
    // indistinguishable from the effect being broken.
    instance.rippleStrength = 0.35 + strange * 0.85;
    return true;
}

gfx.onTierChanged((tier) => {
    if (tier !== 'full') {
        for (const id of [...instances.keys()]) {
            detach(id);
        }
    }
});

window.poseeComicFx = { attach, detach, setReveal, shockwave };
