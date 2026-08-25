// Ink-splatter burst on the score reveal, scaled by strangeness: ~20 particles at 30, ~400 at 95.
//
// Hand-rolled instanced WebGL2 rather than PixiJS. The whole feature is one draw call over a
// two-triangle quad with per-instance attributes — about 5KB of source against ~130KB gzipped
// for a scene graph, texture atlas and filter pipeline this app would never touch. That is a
// first-load cost every visitor pays, for a burst that lasts 1.4 seconds.
//
// Simulation runs in the VERTEX SHADER from immutable per-particle seeds (origin, velocity,
// spin, lifetime). Nothing is written back per frame, so there is no CPU-side particle loop and
// no buffer re-upload — the cost of 400 particles is identical to the cost of 20.

import { gfx, createGl, compileProgram, resizeToDisplay, disposeGl } from './gfx-core.js';

const VERTEX_SHADER = `#version 300 es
precision mediump float;

layout(location = 0) in vec2 aCorner;      // unit quad, -0.5..0.5
layout(location = 1) in vec4 aSeed;        // xy = origin (clip), zw = velocity
layout(location = 2) in vec4 aParams;      // x = size, y = life, z = spin, w = hue

uniform float uTime;
uniform vec2  uResolution;

out vec2  vCorner;
out float vAge;
out float vHue;

void main() {
    float age = uTime / max(aParams.y, 0.001);

    // Past its lifetime the instance is collapsed to zero area. A degenerate triangle is
    // discarded at the rasteriser, which is cheaper than any branch we could write.
    if (age > 1.0) {
        gl_Position = vec4(0.0, 0.0, 2.0, 1.0);
        vAge = 1.0;
        vCorner = aCorner;
        vHue = aParams.w;
        return;
    }

    // Ballistic with drag. Drag matters: without it everything travels in straight lines and
    // reads as a starburst clipart rather than thrown ink.
    float drag = 1.0 - exp(-2.4 * uTime);
    vec2 offset = aSeed.zw * drag * 0.42;
    offset.y -= 0.55 * uTime * uTime;   // gravity, clip-space units

    float angle = aParams.z * uTime;
    mat2 spin = mat2(cos(angle), -sin(angle), sin(angle), cos(angle));

    // Correct for aspect so particles stay round rather than stretching with the viewport.
    float aspect = uResolution.x / max(uResolution.y, 1.0);
    vec2 scaled = spin * aCorner * aParams.x * (1.0 - age * 0.35);
    scaled.x /= aspect;

    gl_Position = vec4(aSeed.xy + offset + scaled, 0.0, 1.0);

    vCorner = aCorner;
    vAge = age;
    vHue = aParams.w;
}`;

const FRAGMENT_SHADER = `#version 300 es
precision mediump float;

in vec2  vCorner;
in float vAge;
in float vHue;
out vec4 fragColor;

uniform vec3 uInk;
uniform vec3 uAccent;

void main() {
    float dist = length(vCorner) * 2.0;
    // Soft-edged blob rather than a hard circle: ink on paper has no crisp boundary.
    float alpha = smoothstep(1.0, 0.35, dist);
    if (alpha <= 0.01) discard;

    // Fade in fast, out slow — an instant appearance reads as a pop, a slow one as a smear.
    float fade = smoothstep(0.0, 0.06, vAge) * (1.0 - smoothstep(0.55, 1.0, vAge));

    vec3 color = mix(uInk, uAccent, vHue);
    fragColor = vec4(color, alpha * fade * 0.92);
}`;

const QUAD = new Float32Array([
    -0.5, -0.5,   0.5, -0.5,   0.5, 0.5,
    -0.5, -0.5,   0.5,  0.5,  -0.5, 0.5
]);

const MAX_PARTICLES = 512;

const instances = new Map();
let nextId = 1;

function parseColor(hex, fallback) {
    const match = /^#?([0-9a-f]{6})$/i.exec((hex ?? '').trim());
    if (!match) return fallback;
    const int = parseInt(match[1], 16);
    return [((int >> 16) & 255) / 255, ((int >> 8) & 255) / 255, (int & 255) / 255];
}

/**
 * Fires one burst on the given canvas. `score` (0-100) sets both the particle count and how
 * wide the spray is. Returns an id, though the burst also tears itself down when it ends.
 */
export function burst(canvas, score = 50, options = {}) {
    if (!canvas || !gfx.allows('full')) {
        return 0;
    }

    const gl = createGl(canvas);
    if (!gl) return 0;

    let program;
    try {
        program = compileProgram(gl, VERTEX_SHADER, FRAGMENT_SHADER);
    } catch (err) {
        console.warn('[particles] shader unavailable; skipping burst', err);
        disposeGl(gl);
        return 0;
    }

    const strength = Math.min(1, Math.max(0, score / 100));
    const count = Math.min(MAX_PARTICLES, Math.round(20 + strength * 380));

    const seeds = new Float32Array(count * 4);
    const params = new Float32Array(count * 4);
    let maxLife = 0;

    for (let i = 0; i < count; i++) {
        // Bias the spray upward: ink thrown at a page, not an explosion in a vacuum.
        const angle = Math.random() * Math.PI * 2;
        const speed = (0.35 + Math.random() * 0.95) * (0.55 + strength * 0.75);

        seeds[i * 4 + 0] = (Math.random() - 0.5) * 0.08;
        seeds[i * 4 + 1] = (Math.random() - 0.5) * 0.08;
        seeds[i * 4 + 2] = Math.cos(angle) * speed;
        seeds[i * 4 + 3] = Math.sin(angle) * speed + 0.45;

        const life = 0.75 + Math.random() * 0.65;
        maxLife = Math.max(maxLife, life);

        params[i * 4 + 0] = 0.012 + Math.random() * (0.020 + strength * 0.030);
        params[i * 4 + 1] = life;
        params[i * 4 + 2] = (Math.random() - 0.5) * 7.0;
        params[i * 4 + 3] = Math.random();
    }

    const vao = gl.createVertexArray();
    gl.bindVertexArray(vao);

    const quadBuffer = gl.createBuffer();
    gl.bindBuffer(gl.ARRAY_BUFFER, quadBuffer);
    gl.bufferData(gl.ARRAY_BUFFER, QUAD, gl.STATIC_DRAW);
    gl.enableVertexAttribArray(0);
    gl.vertexAttribPointer(0, 2, gl.FLOAT, false, 0, 0);

    const seedBuffer = gl.createBuffer();
    gl.bindBuffer(gl.ARRAY_BUFFER, seedBuffer);
    gl.bufferData(gl.ARRAY_BUFFER, seeds, gl.STATIC_DRAW);
    gl.enableVertexAttribArray(1);
    gl.vertexAttribPointer(1, 4, gl.FLOAT, false, 0, 0);
    gl.vertexAttribDivisor(1, 1);

    const paramBuffer = gl.createBuffer();
    gl.bindBuffer(gl.ARRAY_BUFFER, paramBuffer);
    gl.bufferData(gl.ARRAY_BUFFER, params, gl.STATIC_DRAW);
    gl.enableVertexAttribArray(2);
    gl.vertexAttribPointer(2, 4, gl.FLOAT, false, 0, 0);
    gl.vertexAttribDivisor(2, 1);

    gl.bindVertexArray(null);

    const uniforms = {
        time: gl.getUniformLocation(program, 'uTime'),
        resolution: gl.getUniformLocation(program, 'uResolution'),
        ink: gl.getUniformLocation(program, 'uInk'),
        accent: gl.getUniformLocation(program, 'uAccent')
    };

    const ink = parseColor(options.ink, [0.49, 0.23, 0.93]);
    const accent = parseColor(options.accent, [0.96, 0.34, 0.42]);

    const id = nextId++;
    const startTime = performance.now();

    const instance = { gl, program, vao, quadBuffer, seedBuffer, paramBuffer, canvas, stop: null };

    instance.stop = gfx.addTask(`particles#${id}`, (now) => {
        const elapsed = (now - startTime) / 1000;
        if (elapsed > maxLife + 0.1) {
            // Self-terminating: a burst is a one-shot, and leaving its rAF task registered would
            // keep the shared loop awake forever.
            dispose(id);
            return;
        }

        resizeToDisplay(canvas, gl, 2);
        gl.clearColor(0, 0, 0, 0);
        gl.clear(gl.COLOR_BUFFER_BIT);

        gl.enable(gl.BLEND);
        // Premultiplied source: matches the context's premultipliedAlpha, so overlapping
        // particles darken correctly instead of washing to white.
        gl.blendFunc(gl.ONE, gl.ONE_MINUS_SRC_ALPHA);

        gl.useProgram(program);
        gl.bindVertexArray(vao);
        gl.uniform1f(uniforms.time, elapsed);
        gl.uniform2f(uniforms.resolution, canvas.width, canvas.height);
        gl.uniform3fv(uniforms.ink, ink);
        gl.uniform3fv(uniforms.accent, accent);
        gl.drawArraysInstanced(gl.TRIANGLES, 0, 6, count);
        gl.bindVertexArray(null);
    });

    instances.set(id, instance);
    return id;
}

export function dispose(id) {
    const instance = instances.get(id);
    if (!instance) return;

    instance.stop?.();
    const { gl } = instance;
    gl.deleteBuffer(instance.quadBuffer);
    gl.deleteBuffer(instance.seedBuffer);
    gl.deleteBuffer(instance.paramBuffer);
    gl.deleteVertexArray(instance.vao);
    gl.deleteProgram(instance.program);
    disposeGl(gl);
    instances.delete(id);
}

gfx.onTierChanged((tier) => {
    if (tier !== 'full') {
        for (const id of [...instances.keys()]) {
            dispose(id);
        }
    }
});

window.poseeParticles = { burst, dispose };
