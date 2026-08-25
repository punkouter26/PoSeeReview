// Shader loading ring for the generation stepper. Replaces the SVG arc with a liquid band that
// churns harder as the pipeline advances.
//
// It is driven by the REAL phase events streamed from the API, not a timer — the whole point of
// the streaming endpoint was to stop the progress display from guessing. `setProgress` is called
// with the actual completed fraction; the shader only interpolates between the values it is given.

import { gfx, createGl, compileProgram, FULLSCREEN_VERTEX_SHADER, resizeToDisplay, disposeGl } from './gfx-core.js';

const FRAGMENT_SHADER = `#version 300 es
precision mediump float;

in vec2 vUv;
out vec4 fragColor;

uniform float uTime;
uniform float uProgress;   // 0..1, the fraction of the pipeline actually completed
uniform vec3  uColorA;
uniform vec3  uColorB;

float hash(vec2 p) {
    p = fract(p * vec2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return fract(p.x * p.y);
}

float valueNoise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    vec2 u = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(i), hash(i + vec2(1, 0)), u.x),
               mix(hash(i + vec2(0, 1)), hash(i + vec2(1, 1)), u.x), u.y);
}

const float PI = 3.14159265;

void main() {
    vec2 p = vUv - 0.5;
    float radius = length(p);
    float angle = atan(p.y, p.x);

    // Rotate the zero point to 12 o'clock and normalise to 0..1 clockwise, so the band fills
    // the way every other progress ring on the platform does.
    float t = fract((PI * 0.5 - angle) / (PI * 2.0) + 1.0);

    // Liquid wobble on the band's inner and outer edges.
    float churn = 0.35 + uProgress * 0.9;
    float wobble = (valueNoise(vec2(t * 9.0, uTime * (0.6 + uProgress))) - 0.5) * 0.022 * churn;

    float inner = 0.325 + wobble;
    float outer = 0.445 - wobble;
    float band = smoothstep(inner - 0.012, inner + 0.012, radius)
               * (1.0 - smoothstep(outer - 0.012, outer + 0.012, radius));
    if (band <= 0.001) discard;

    // Filled portion, with a soft leading edge so the head of the band reads as liquid.
    float filled = 1.0 - smoothstep(uProgress - 0.015, uProgress + 0.015, t);

    vec3 color = mix(uColorA, uColorB, t);

    // A highlight that runs around the completed arc — the thing that makes it feel alive while
    // the numbers are not changing.
    float sweep = fract(t - uTime * 0.28);
    color += vec3(0.35) * pow(1.0 - sweep, 14.0) * filled;

    // Unfilled track stays visible but dim, so the ring reads as a whole circle.
    float alpha = band * mix(0.16, 1.0, filled);
    fragColor = vec4(color, alpha);
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
    if (!gl) return 0;

    let program;
    try {
        program = compileProgram(gl, FULLSCREEN_VERTEX_SHADER, FRAGMENT_SHADER);
    } catch (err) {
        // The SVG ring underneath is still there and still correct.
        console.warn('[loading-ring] shader unavailable; keeping the SVG ring', err);
        disposeGl(gl);
        return 0;
    }

    const uniforms = {
        time: gl.getUniformLocation(program, 'uTime'),
        progress: gl.getUniformLocation(program, 'uProgress'),
        colorA: gl.getUniformLocation(program, 'uColorA'),
        colorB: gl.getUniformLocation(program, 'uColorB')
    };

    const vao = gl.createVertexArray();
    const id = nextId++;
    const startTime = performance.now();

    const instance = {
        gl, program, vao, canvas,
        progress: 0,
        targetProgress: Math.min(1, Math.max(0, options.progress ?? 0)),
        colorA: parseColor(options.colorA, [0.49, 0.23, 0.93]),
        colorB: parseColor(options.colorB, [0.96, 0.34, 0.42]),
        stop: null
    };

    instance.stop = gfx.addTask(`loading-ring#${id}`, (now) => {
        resizeToDisplay(canvas, gl, 2);

        // Ease toward the reported phase. Phases arrive as discrete jumps seconds apart; snapping
        // would make the ring look broken between them.
        instance.progress += (instance.targetProgress - instance.progress) * 0.07;

        gl.clearColor(0, 0, 0, 0);
        gl.clear(gl.COLOR_BUFFER_BIT);
        gl.enable(gl.BLEND);
        gl.blendFunc(gl.ONE, gl.ONE_MINUS_SRC_ALPHA);

        gl.useProgram(program);
        gl.bindVertexArray(vao);
        gl.uniform1f(uniforms.time, (now - startTime) / 1000);
        gl.uniform1f(uniforms.progress, instance.progress);
        gl.uniform3fv(uniforms.colorA, instance.colorA);
        gl.uniform3fv(uniforms.colorB, instance.colorB);
        gl.drawArrays(gl.TRIANGLES, 0, 3);
        gl.bindVertexArray(null);
    });

    instances.set(id, instance);
    canvas.dataset.ringFx = 'on';
    return id;
}

export function setProgress(id, progress) {
    const instance = instances.get(id);
    if (instance) {
        instance.targetProgress = Math.min(1, Math.max(0, progress ?? 0));
    }
}

export function stop(id) {
    const instance = instances.get(id);
    if (!instance) return;

    instance.stop?.();
    instance.gl.deleteProgram(instance.program);
    instance.gl.deleteVertexArray(instance.vao);
    disposeGl(instance.gl);
    if (instance.canvas) {
        delete instance.canvas.dataset.ringFx;
    }
    instances.delete(id);
}

gfx.onTierChanged((tier) => {
    if (tier !== 'full') {
        for (const id of [...instances.keys()]) {
            stop(id);
        }
    }
});

window.poseeLoadingRing = { start, stop, setProgress };
