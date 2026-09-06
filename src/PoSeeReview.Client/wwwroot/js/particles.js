// Ink-splatter burst on the score reveal, scaled by strangeness: ~40 particles at 30, ~1500 at 95.
//
// Hand-rolled instanced WebGL2 rather than PixiJS. The whole feature is one draw call over a
// two-triangle quad with per-instance attributes — about 6KB of source against ~130KB gzipped
// for a scene graph, texture atlas and filter pipeline this app would never touch. That is a
// first-load cost every visitor pays, for a burst that lasts 1.4 seconds.
//
// Simulation runs in the VERTEX SHADER from immutable per-particle seeds (origin, velocity,
// spin, lifetime). Nothing is written back per frame, so there is no CPU-side particle loop and
// no buffer re-upload — the cost of 1500 particles is identical to the cost of 20. That is the
// whole reason the count could be raised fourfold here without touching the frame budget: the
// cost is fill rate, and the particles shrink as they multiply.
//
// Three things were added on top of the original ballistic sim, in order of how much they show:
//
//   * TURBULENCE. Pure ballistics with drag reads as a firework. Real thrown ink curls, because
//     the air it moves through is not still. The field is analytic — sin/cos of the particle's
//     own position and age — so it stays stateless and costs a handful of ALU in the vertex
//     shader, where there are 1500 invocations rather than a million.
//   * MOTION STRETCH. Each quad is elongated along its own velocity, by an amount that decays
//     as the particle slows. This is what makes a fast blob read as a streak of ink instead of
//     a circle that happens to be moving.
//   * LIGHTING. The fragment shader treats each blob as a hemisphere and lights it. A flat-shaded
//     disc is a dot; a lit one is a droplet.

import { gfx, createSurface, compileProgram } from './gfx-core.js';

const VERTEX_SHADER = `#version 300 es
precision highp float;

layout(location = 0) in vec2 aCorner;      // unit quad, -0.5..0.5
layout(location = 1) in vec4 aSeed;        // xy = origin (clip), zw = velocity
layout(location = 2) in vec4 aParams;      // x = size, y = life, z = spin, w = hue

uniform float uTime;
uniform vec2  uResolution;
uniform float uTurbulence;

out vec2  vCorner;
out float vAge;
out float vHue;
out float vStretch;

/**
 * Analytic turbulence. Not curl noise — curl noise needs a noise field and its derivatives, and
 * this runs per particle per frame where the budget is measured in a few dozen ALU. Two
 * perpendicular sine lobes at incommensurate frequencies give a field that is divergence-light,
 * has no visible grid, and does not repeat over the 1.4 seconds anyone sees it.
 */
vec2 turbulence(vec2 position, float t) {
    return vec2(
        sin(position.y * 6.1 + t * 2.3) + 0.5 * sin(position.y * 13.7 - t * 1.7),
        cos(position.x * 5.7 - t * 1.9) + 0.5 * cos(position.x * 12.3 + t * 2.1)
    );
}

void main() {
    float age = uTime / max(aParams.y, 0.001);

    // Past its lifetime the instance is collapsed to zero area. A degenerate triangle is
    // discarded at the rasteriser, which is cheaper than any branch we could write.
    if (age > 1.0) {
        gl_Position = vec4(0.0, 0.0, 2.0, 1.0);
        vAge = 1.0;
        vCorner = aCorner;
        vHue = aParams.w;
        vStretch = 0.0;
        return;
    }

    // Ballistic with drag. Drag matters: without it everything travels in straight lines and
    // reads as a starburst clipart rather than thrown ink.
    float drag = 1.0 - exp(-2.4 * uTime);
    vec2 offset = aSeed.zw * drag * 0.42;
    offset.y -= 0.55 * uTime * uTime;   // gravity, clip-space units

    // Turbulence ramps in rather than applying from t=0: at the instant of the burst every
    // particle is still inside the impact, and curling there would hide the radial spray that
    // reads as an impact in the first place.
    vec2 position = aSeed.xy + offset;
    float swirl = uTurbulence * smoothstep(0.0, 0.25, uTime) * (1.0 - age * 0.5);
    offset += turbulence(position, uTime) * swirl * 0.035;

    // Instantaneous velocity, for the stretch. Analytic derivative of the ballistic term — a
    // finite difference would need a second evaluation of everything above.
    vec2 velocity = aSeed.zw * 2.4 * exp(-2.4 * uTime) * 0.42;
    velocity.y -= 1.10 * uTime;
    float speed = length(velocity);

    float angle = aParams.z * uTime;
    mat2 spin = mat2(cos(angle), -sin(angle), sin(angle), cos(angle));

    // Correct for aspect so particles stay round rather than stretching with the viewport.
    float aspect = uResolution.x / max(uResolution.y, 1.0);
    vec2 scaled = spin * aCorner * aParams.x * (1.0 - age * 0.35);

    // Motion stretch. The quad is elongated along the velocity direction and narrowed across it,
    // preserving area — a stretch that also grows the blob would make fast particles read as
    // bigger drops rather than faster ones.
    float stretch = clamp(speed * 0.75, 0.0, 1.6);
    if (stretch > 0.01) {
        vec2 dir = velocity / max(speed, 0.0001);
        vec2 perp = vec2(-dir.y, dir.x);
        float along = dot(scaled, dir) * (1.0 + stretch);
        float across = dot(scaled, perp) / (1.0 + stretch * 0.5);
        scaled = dir * along + perp * across;
    }

    scaled.x /= aspect;
    gl_Position = vec4(position + scaled, 0.0, 1.0);

    vCorner = aCorner;
    vAge = age;
    vHue = aParams.w;
    vStretch = stretch;
}`;

const FRAGMENT_SHADER = `#version 300 es
precision mediump float;

in vec2  vCorner;
in float vAge;
in float vHue;
in float vStretch;
out vec4 fragColor;

uniform vec3 uInk;
uniform vec3 uAccent;
uniform vec3 uLightDir;

void main() {
    float dist = length(vCorner) * 2.0;
    // Soft-edged blob rather than a hard circle: ink on paper has no crisp boundary.
    float alpha = smoothstep(1.0, 0.35, dist);
    if (alpha <= 0.01) discard;

    // Fade in fast, out slow — an instant appearance reads as a pop, a slow one as a smear.
    float fade = smoothstep(0.0, 0.06, vAge) * (1.0 - smoothstep(0.55, 1.0, vAge));

    vec3 color = mix(uInk, uAccent, vHue);

    // Treat the blob as a hemisphere: reconstruct a normal from its position within the quad and
    // light it. This is the same trick point-sprite renderers use, and it costs one sqrt.
    // A droplet with a highlight reads as wet ink; a flat disc reads as a dot.
    float z = sqrt(max(0.0, 1.0 - min(1.0, dist * dist)));
    vec3 normal = normalize(vec3(vCorner * 2.0, z));

    float diffuse = max(0.0, dot(normal, uLightDir));
    vec3 halfway = normalize(uLightDir + vec3(0.0, 0.0, 1.0));
    float specular = pow(max(0.0, dot(normal, halfway)), 24.0);

    // Wet ink is dark where it is thick and bright only at the highlight. Weighting diffuse
    // below 1.0 keeps the body of the drop saturated instead of washing it toward the light.
    color *= 0.55 + diffuse * 0.65;
    color += specular * 0.35 * (1.0 - vAge);

    // A stretched particle is thinner, so it carries less ink and is more translucent.
    float thinning = 1.0 / (1.0 + vStretch * 0.35);

    fragColor = vec4(color, alpha * fade * thinning * 0.92);
}`;

const QUAD = new Float32Array([
    -0.5, -0.5,   0.5, -0.5,   0.5, 0.5,
    -0.5, -0.5,   0.5,  0.5,  -0.5, 0.5
]);

// Raised from 512. The simulation is stateless and lives in the vertex shader, so the marginal
// cost of an instance is its rasterised area — and particles shrink as the count rises, so the
// total ink coverage stays roughly constant. What changes is density, which is the whole point.
const MAX_PARTICLES = 2048;

const instances = new Map();
let nextId = 1;

function parseColor(hex, fallback) {
    const match = /^#?([0-9a-f]{6})$/i.exec((hex ?? '').trim());
    if (!match) return fallback;
    const int = parseInt(match[1], 16);
    return [((int >> 16) & 255) / 255, ((int >> 8) & 255) / 255, (int & 255) / 255];
}

/**
 * Fires one burst on the given canvas. `score` (0-100) sets the particle count, the spray width
 * and how much the ink curls. Returns an id, though the burst also tears itself down when it ends.
 */
export function burst(canvas, score = 50, options = {}) {
    if (!canvas || !gfx.allows('full')) {
        return 0;
    }

    const surface = createSurface(canvas, { maxDpr: 2 });
    if (!surface) return 0;

    const { gl } = surface;

    let program;
    try {
        program = compileProgram(gl, VERTEX_SHADER, FRAGMENT_SHADER);
    } catch (err) {
        console.warn('[particles] shader unavailable; skipping burst', err);
        surface.release();
        return 0;
    }

    const strength = Math.min(1, Math.max(0, score / 100));
    const count = Math.min(MAX_PARTICLES, Math.round(40 + strength * 1460));

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

        // Sizes shrink as the count rises so a high-strangeness burst reads as a fine mist of
        // ink rather than a wall of overlapping blobs that blows out the fill rate.
        const sizeScale = 1 / (1 + strength * 1.1);
        params[i * 4 + 0] = (0.012 + Math.random() * (0.020 + strength * 0.030)) * sizeScale;
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
        accent: gl.getUniformLocation(program, 'uAccent'),
        turbulence: gl.getUniformLocation(program, 'uTurbulence'),
        lightDir: gl.getUniformLocation(program, 'uLightDir')
    };

    const ink = parseColor(options.ink, [0.49, 0.23, 0.93]);
    const accent = parseColor(options.accent, [0.96, 0.34, 0.42]);
    // Upper-left key, matching the paper lighting on the comic panel next to it. Two decorative
    // layers lit from opposite directions is the sort of thing nobody names but everybody sees.
    const lightDir = (() => {
        const v = [-0.45, 0.55, 0.70];
        const length = Math.hypot(...v);
        return v.map((component) => component / length);
    })();

    const id = nextId++;
    const startTime = performance.now();

    const instance = {
        gl, surface, program, vao, quadBuffer, seedBuffer, paramBuffer, canvas, stop: null
    };

    instance.stop = gfx.addTask(`particles#${id}`, (now) => {
        const elapsed = (now - startTime) / 1000;
        if (elapsed > maxLife + 0.1) {
            // Self-terminating: a burst is a one-shot, and leaving its rAF task registered would
            // keep the shared loop awake forever.
            dispose(id);
            return;
        }

        if (!surface.beginFrame()) {
            dispose(id);
            return;
        }

        gl.clearColor(0, 0, 0, 0);
        gl.clear(gl.COLOR_BUFFER_BIT);

        gl.enable(gl.BLEND);
        // Premultiplied source: matches the context's premultipliedAlpha, so overlapping
        // particles darken correctly instead of washing to white.
        gl.blendFunc(gl.ONE, gl.ONE_MINUS_SRC_ALPHA);

        gl.useProgram(program);
        gl.bindVertexArray(vao);
        gl.uniform1f(uniforms.time, elapsed);
        gl.uniform2f(uniforms.resolution, surface.width, surface.height);
        gl.uniform3fv(uniforms.ink, ink);
        gl.uniform3fv(uniforms.accent, accent);
        gl.uniform1f(uniforms.turbulence, 0.35 + strength * 0.95);
        gl.uniform3fv(uniforms.lightDir, lightDir);
        gl.drawArraysInstanced(gl.TRIANGLES, 0, 6, count);
        gl.bindVertexArray(null);

        // The pool's context is shared, so blend state left enabled here would silently change
        // how the next effect composites. Restore it rather than making every other module defend.
        gl.disable(gl.BLEND);

        surface.present();
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
    instance.surface.release();
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
