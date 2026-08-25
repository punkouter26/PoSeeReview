// Strangeness-reactive background. A single fullscreen triangle running domain-warped value
// noise, with hue, warp strength and speed driven by the current comic's score — so a 92 visibly
// seethes and a 30 drifts.
//
// Cost control, in order of how much they matter:
//   * DPR is capped at 1.25 here (lower than the shared default). This is an out-of-focus
//     backdrop; nobody can see the difference, and fill rate is the entire cost of the pass.
//   * The whole thing runs on the shared scheduler, so it pauses with the tab.
//   * 'full' tier only. At 'lite' the CSS gradient underneath is what shows.

import { gfx, createGl, compileProgram, FULLSCREEN_VERTEX_SHADER, resizeToDisplay, disposeGl } from './gfx-core.js';

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

void main() {
    vec2 uv = vUv;
    // Correct for aspect so the noise cells stay round on a wide viewport.
    vec2 p = uv * vec2(uResolution.x / max(uResolution.y, 1.0), 1.0);

    float speed = 0.02 + uStrange * 0.10;
    float t = uTime * speed;

    // Domain warping: sampling noise at a position that is itself offset by noise. This is what
    // turns smooth blobs into something that looks like it is churning.
    float warpAmount = 0.15 + uStrange * 0.85;
    vec2 warp = vec2(
        fbm(p * 1.4 + vec2(t, t * 0.7)),
        fbm(p * 1.4 + vec2(-t * 0.8, t * 1.1) + 5.2)
    );
    float n = fbm(p * 2.1 + warp * warpAmount + t * 0.35);

    vec3 color = mix(uColorA, uColorB, smoothstep(0.25, 0.75, n));
    color = mix(color, uColorC, smoothstep(0.55, 1.0, n) * (0.25 + uStrange * 0.55));

    // Vignette keeps the centre of the page readable; content sits on top of this.
    float vignette = smoothstep(1.25, 0.25, length(uv - 0.5) * 1.4);
    color *= 0.82 + vignette * 0.18;

    // Dithering. Without it, a slow wide gradient shows visible banding on 8-bit displays and
    // the whole effect reads as a compression artifact.
    float dither = (hash(gl_FragCoord.xy) - 0.5) / 255.0;
    fragColor = vec4(color + dither, 1.0);
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

    const gl = createGl(canvas);
    if (!gl) {
        return 0;
    }

    let program;
    try {
        program = compileProgram(gl, FULLSCREEN_VERTEX_SHADER, FRAGMENT_SHADER);
    } catch (err) {
        // A driver that refuses this shader is not an app error — the CSS gradient stays.
        console.warn('[gradient] shader unavailable; falling back to CSS', err);
        disposeGl(gl);
        return 0;
    }

    const uniforms = {
        time: gl.getUniformLocation(program, 'uTime'),
        resolution: gl.getUniformLocation(program, 'uResolution'),
        strange: gl.getUniformLocation(program, 'uStrange'),
        colorA: gl.getUniformLocation(program, 'uColorA'),
        colorB: gl.getUniformLocation(program, 'uColorB'),
        colorC: gl.getUniformLocation(program, 'uColorC')
    };

    // WebGL2 requires a bound VAO even when every vertex is generated from gl_VertexID.
    const vao = gl.createVertexArray();

    const id = nextId++;
    const instance = {
        gl,
        program,
        vao,
        canvas,
        startTime: performance.now(),
        strange: Math.min(1, Math.max(0, (options.score ?? 40) / 100)),
        targetStrange: Math.min(1, Math.max(0, (options.score ?? 40) / 100)),
        colorA: parseColor(options.colorA, [0.10, 0.03, 0.20]),
        colorB: parseColor(options.colorB, [0.29, 0.12, 0.58]),
        colorC: parseColor(options.colorC, [0.96, 0.34, 0.42]),
        stop: null
    };

    instance.stop = gfx.addTask(`gradient#${id}`, (now) => {
        resizeToDisplay(canvas, gl, 1.25);

        // Ease toward the target so a score change is a transition, not a jump cut.
        instance.strange += (instance.targetStrange - instance.strange) * 0.04;

        gl.useProgram(program);
        gl.bindVertexArray(vao);
        gl.uniform1f(uniforms.time, (now - instance.startTime) / 1000);
        gl.uniform2f(uniforms.resolution, canvas.width, canvas.height);
        gl.uniform1f(uniforms.strange, instance.strange);
        gl.uniform3fv(uniforms.colorA, instance.colorA);
        gl.uniform3fv(uniforms.colorB, instance.colorB);
        gl.uniform3fv(uniforms.colorC, instance.colorC);
        gl.drawArrays(gl.TRIANGLES, 0, 3);
        gl.bindVertexArray(null);
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

export function stop(id) {
    const instance = instances.get(id);
    if (!instance) return;

    instance.stop?.();
    instance.gl.deleteProgram(instance.program);
    instance.gl.deleteVertexArray(instance.vao);
    disposeGl(instance.gl);
    instances.set(id, null);
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
