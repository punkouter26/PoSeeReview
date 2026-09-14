// The ink burst, simulated in a compute shader.
//
// WHAT THIS DOES THAT particles.js CANNOT.
//
// The WebGL2 burst evaluates every particle's whole trajectory in the vertex shader from
// immutable seeds. That is why 1500 particles cost the same as 20 — nothing is written back, so
// there is no per-frame state and no CPU loop. It is also why no particle can ever react to
// anything: not to the floor, not to a later frame's conditions, not to each other. A ballistic
// curve is all a stateless sim can express.
//
// A compute pass writes state back to a storage buffer, so this version integrates properly:
// drops decelerate through drag, hit the bottom of the panel, lose most of their energy, and
// SETTLE. The visible difference is the ending. The WebGL2 burst fades out mid-air because it has
// to; this one lands.
//
// WHY THE STATE MACHINE IS IN THE SHADER AND NOT IN JS. Reading a storage buffer back to decide
// which particles have settled would stall the pipeline on a mapAsync every frame — a round trip
// that costs more than the entire simulation. Each particle carries its own `settled` flag in the
// buffer instead, and the CPU never reads any of it. The only thing JS knows is how long the
// burst has been running.
//
// FALLS BACK, NEVER FAILS. Every entry point returns 0/null when WebGPU is unavailable, and
// particles.js runs its WebGL2 path instead. See webgpu-pool.js for why the device is shared.

import { gfx } from './gfx-core.js';
import { acquireDevice, configureCanvas } from './webgpu-pool.js';

const MAX_PARTICLES = 2048;
const WORKGROUP_SIZE = 64;

// pos(2) vel(2) size hue age settled = 8 floats.
const FLOATS_PER_PARTICLE = 8;
const BYTES_PER_PARTICLE = FLOATS_PER_PARTICLE * 4;

const SHADER = /* wgsl */ `
struct Particle {
    pos:     vec2<f32>,
    vel:     vec2<f32>,
    size:    f32,
    hue:     f32,
    age:     f32,
    settled: f32,
};

struct Uniforms {
    resolution:  vec2<f32>,
    dt:          f32,
    time:        f32,
    gravity:     f32,
    drag:        f32,
    turbulence:  f32,
    count:       f32,
};

@group(0) @binding(0) var<storage, read_write> particles: array<Particle>;
@group(0) @binding(1) var<uniform> u: Uniforms;

// Analytic curl-ish field. Identical in spirit to the WebGL2 version's turbulence: real thrown
// ink curls because the air it moves through is not still, and pure ballistics reads as a
// firework. Stateless and a handful of ALU, so it costs nothing at this particle count.
fn turbulence(p: vec2<f32>, t: f32) -> vec2<f32> {
    let q = p * 0.011;
    return vec2<f32>(
        sin(q.y * 2.3 + t * 1.7) * cos(q.x * 1.1 - t * 0.6),
        cos(q.x * 2.7 - t * 1.3) * sin(q.y * 0.9 + t * 0.8)
    );
}

@compute @workgroup_size(${WORKGROUP_SIZE})
fn simulate(@builtin(global_invocation_id) gid: vec3<u32>) {
    let index = gid.x;
    if (f32(index) >= u.count) {
        return;
    }

    var p = particles[index];
    p.age = p.age + u.dt;

    // A settled drop is done. Skipping it here is most of why this stays cheap once the burst
    // has landed: within about a second the great majority of invocations return immediately.
    if (p.settled > 0.5) {
        particles[index] = p;
        return;
    }

    let turb = turbulence(p.pos, u.time) * u.turbulence;

    p.vel = p.vel + (vec2<f32>(0.0, u.gravity) + turb) * u.dt;
    // Exponential drag, framerate independent. A per-frame multiply would make the burst travel
    // further on a fast device than on a slow one.
    p.vel = p.vel * exp(-u.drag * u.dt);
    p.pos = p.pos + p.vel * u.dt;

    let floorY = u.resolution.y - p.size;

    if (p.pos.y >= floorY) {
        p.pos.y = floorY;
        // Ink, not rubber. Most of the vertical energy is absorbed and the horizontal component
        // is scrubbed hard, so a drop spreads slightly and stops rather than skating.
        p.vel.y = -p.vel.y * 0.18;
        p.vel.x = p.vel.x * 0.55;

        // Below this the bounce is smaller than a pixel; leaving it running would keep the whole
        // buffer awake forever resolving motion nobody can see.
        if (abs(p.vel.y) < 26.0) {
            p.settled = 1.0;
            p.vel = vec2<f32>(0.0, 0.0);
        }
    }

    // Side walls absorb rather than reflect. A drop bouncing off the edge of a comic panel reads
    // as a ball in a box; ink hitting the edge of a page simply stops there.
    if (p.pos.x < p.size) {
        p.pos.x = p.size;
        p.vel.x = abs(p.vel.x) * 0.25;
    } else if (p.pos.x > u.resolution.x - p.size) {
        p.pos.x = u.resolution.x - p.size;
        p.vel.x = -abs(p.vel.x) * 0.25;
    }

    particles[index] = p;
}

// ── Render ───────────────────────────────────────────────────────────────────────────────

struct VertexOut {
    @builtin(position) position: vec4<f32>,
    @location(0)       corner:   vec2<f32>,
    @location(1)       hue:      f32,
    @location(2)       fade:     f32,
};

@vertex
fn vs(@builtin(vertex_index) vertexIndex: u32,
      @builtin(instance_index) instanceIndex: u32) -> VertexOut {
    let p = particles[instanceIndex];

    // Unit quad from two triangles, indexless.
    var corners = array<vec2<f32>, 6>(
        vec2<f32>(-0.5, -0.5), vec2<f32>(0.5, -0.5), vec2<f32>(-0.5, 0.5),
        vec2<f32>(-0.5,  0.5), vec2<f32>(0.5, -0.5), vec2<f32>( 0.5, 0.5)
    );
    let corner = corners[vertexIndex];

    // Motion stretch: elongate along velocity so a fast drop reads as a streak of ink rather
    // than a circle that happens to be moving. Decays with speed, and a settled drop is round.
    let speed = length(p.vel);
    let stretch = 1.0 + min(speed * 0.0016, 1.8) * (1.0 - p.settled);
    var axis = vec2<f32>(1.0, 0.0);
    if (speed > 1.0) {
        axis = p.vel / speed;
    }
    let normal = vec2<f32>(-axis.y, axis.x);

    let local = axis * (corner.x * p.size * 2.0 * stretch)
              + normal * (corner.y * p.size * 2.0);
    let pixel = p.pos + local;

    // Pixel space to clip space. Y flips because the simulation runs in image coordinates, where
    // down is positive — the same convention the canvas and the floor collision use.
    let clip = vec2<f32>(
        (pixel.x / u.resolution.x) * 2.0 - 1.0,
        1.0 - (pixel.y / u.resolution.y) * 2.0
    );

    var out: VertexOut;
    out.position = vec4<f32>(clip, 0.0, 1.0);
    out.corner = corner * 2.0;
    out.hue = p.hue;
    // Settled ink stays put and stays visible; only airborne ink thins as it ages. The whole
    // burst is faded out by the host at the end, so this is texture rather than the exit.
    out.fade = select(clamp(1.0 - p.age * 0.35, 0.0, 1.0), 1.0, p.settled > 0.5);
    return out;
}

@fragment
fn fs(in: VertexOut) -> @location(0) vec4<f32> {
    let d = length(in.corner);
    if (d > 1.0) {
        discard;
    }

    // Hemisphere lighting. A flat disc is a dot; a lit one is a droplet, and that single term is
    // most of the difference between "particles" and "ink".
    let z = sqrt(max(0.0, 1.0 - d * d));
    let normal = normalize(vec3<f32>(in.corner, z));
    let lightDir = normalize(vec3<f32>(-0.4, -0.7, 0.6));
    let diffuse = max(0.0, dot(normal, lightDir));
    let specular = pow(diffuse, 24.0) * 0.5;

    // Brand purple through to accent red, indexed by the particle's own hue seed.
    let cool = vec3<f32>(0.486, 0.227, 0.929);
    let warm = vec3<f32>(0.961, 0.341, 0.424);
    var color = mix(cool, warm, in.hue) * (0.45 + diffuse * 0.75) + specular;

    let alpha = smoothstep(1.0, 0.72, d) * in.fade;
    // Premultiplied: the canvas is configured that way so it can sit over the comic.
    return vec4<f32>(color * alpha, alpha);
}
`;

const instances = new Map();
let nextId = 1;

/** Seeds the buffer on the CPU once. Nothing is uploaded again after this. */
function seedParticles(count, width, height, originX, originY, strange) {
    const data = new Float32Array(count * FLOATS_PER_PARTICLE);

    for (let i = 0; i < count; i++) {
        const o = i * FLOATS_PER_PARTICLE;
        const angle = Math.random() * Math.PI * 2;

        // sqrt on the random radius gives a uniform disc rather than a centre-heavy one, so the
        // burst reads as a spray and not as a clump with outliers.
        const speed = (180 + Math.sqrt(Math.random()) * 900) * (0.55 + strange * 0.85);

        data[o + 0] = originX + Math.cos(angle) * 3;
        data[o + 1] = originY + Math.sin(angle) * 3;
        data[o + 2] = Math.cos(angle) * speed;
        // Biased upward. Ink flicked at a page goes up before it comes down, and a burst that
        // only ever falls reads as a leak.
        data[o + 3] = Math.sin(angle) * speed - 260 * (0.5 + strange);
        // Smaller as the count rises: the cost here is fill rate, so more drops must be finer
        // ones or a high score simply floods the panel.
        data[o + 4] = Math.max(1.2, (Math.min(width, height) * 0.012) * (0.4 + Math.random() * 1.3)
            * (1 - Math.min(0.55, count / 4000)));
        data[o + 5] = Math.random();
        data[o + 6] = 0;
        data[o + 7] = 0;
    }

    return data;
}

/**
 * Starts a compute-simulated burst.
 *
 * @returns {Promise<number>} handle, or 0 when WebGPU is unavailable — in which case the caller
 *          must run its WebGL2 path. This never throws.
 */
export async function burst(canvas, options = {}) {
    if (!canvas || !gfx.allows('full')) return 0;

    const acquired = await acquireDevice();
    if (!acquired) return 0;
    const { device, format } = acquired;

    const target = configureCanvas(canvas, device, format, 2);
    if (!target) return 0;

    let size = target.resize();
    const score = options.score ?? 50;
    const strange = Math.min(1, Math.max(0, score / 100));
    const count = Math.min(MAX_PARTICLES, Math.round(40 + strange * strange * 1460));

    let module, computePipeline, renderPipeline, particleBuffer, uniformBuffer, bindGroup;
    try {
        module = device.createShaderModule({ code: SHADER, label: 'posee-particles' });

        // One bind group layout shared by both pipelines: the compute pass and the vertex stage
        // read the same storage buffer, and a shared explicit layout is what lets one bind group
        // serve both without rebinding between passes.
        const bindGroupLayout = device.createBindGroupLayout({
            entries: [
                {
                    binding: 0,
                    visibility: GPUShaderStage.COMPUTE | GPUShaderStage.VERTEX,
                    buffer: { type: 'storage' }
                },
                {
                    binding: 1,
                    visibility: GPUShaderStage.COMPUTE | GPUShaderStage.VERTEX | GPUShaderStage.FRAGMENT,
                    buffer: { type: 'uniform' }
                }
            ]
        });
        const layout = device.createPipelineLayout({ bindGroupLayouts: [bindGroupLayout] });

        computePipeline = device.createComputePipeline({
            layout,
            compute: { module, entryPoint: 'simulate' }
        });

        renderPipeline = device.createRenderPipeline({
            layout,
            vertex: { module, entryPoint: 'vs' },
            fragment: {
                module,
                entryPoint: 'fs',
                targets: [{
                    format,
                    blend: {
                        // Premultiplied source-over. Straight alpha here would darken every
                        // overlap into a halo, which on a burst of 1500 is most of the frame.
                        color: { srcFactor: 'one', dstFactor: 'one-minus-src-alpha' },
                        alpha: { srcFactor: 'one', dstFactor: 'one-minus-src-alpha' }
                    }
                }]
            },
            primitive: { topology: 'triangle-list' }
        });

        particleBuffer = device.createBuffer({
            size: count * BYTES_PER_PARTICLE,
            usage: GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_DST
        });
        device.queue.writeBuffer(particleBuffer, 0, seedParticles(
            count, size.width, size.height,
            (options.originX ?? 0.5) * size.width,
            (options.originY ?? 0.5) * size.height,
            strange));

        uniformBuffer = device.createBuffer({
            size: 32,     // 8 floats, and already a multiple of the 16-byte minimum.
            usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST
        });

        bindGroup = device.createBindGroup({
            layout: bindGroupLayout,
            entries: [
                { binding: 0, resource: { buffer: particleBuffer } },
                { binding: 1, resource: { buffer: uniformBuffer } }
            ]
        });
    } catch (err) {
        console.warn('[particles-gpu] pipeline unavailable; falling back to WebGL2', err);
        target.release();
        return 0;
    }

    const id = nextId++;
    const uniforms = new Float32Array(8);
    const startedAt = performance.now();
    const lifetimeMs = options.lifetimeMs ?? 3200;
    const fadeMs = 700;

    const instance = {
        canvas,
        target,
        buffers: [particleBuffer, uniformBuffer],
        stop: null
    };

    instance.stop = gfx.addTask(`particles-gpu#${id}`, (now, elapsed) => {
        const age = now - startedAt;
        if (age > lifetimeMs) {
            stop(id);
            return;
        }

        size = target.resize();

        // Clamped, for the same reason the Verlet solver clamps: a throttled tab hands back a
        // several-hundred-millisecond elapsed, and integrating that in one step fires every
        // particle through the floor before the collision test can see it.
        const dt = Math.min(1 / 30, Math.max(1 / 240, (elapsed || 16.7) / 1000));

        uniforms[0] = size.width;
        uniforms[1] = size.height;
        uniforms[2] = dt;
        uniforms[3] = age / 1000;
        uniforms[4] = 2200;                        // gravity, px/s²
        uniforms[5] = 1.35;                        // drag coefficient
        uniforms[6] = 260 + strange * 520;         // turbulence, stronger for stranger comics
        uniforms[7] = count;
        device.queue.writeBuffer(uniformBuffer, 0, uniforms);

        let view;
        try {
            view = target.context.getCurrentTexture().createView();
        } catch {
            // The canvas was detached or the device went away mid-frame.
            stop(id);
            return;
        }

        const encoder = device.createCommandEncoder();

        const compute = encoder.beginComputePass();
        compute.setPipeline(computePipeline);
        compute.setBindGroup(0, bindGroup);
        compute.dispatchWorkgroups(Math.ceil(count / WORKGROUP_SIZE));
        compute.end();

        const render = encoder.beginRenderPass({
            colorAttachments: [{
                view,
                clearValue: { r: 0, g: 0, b: 0, a: 0 },
                loadOp: 'clear',
                storeOp: 'store'
            }]
        });
        render.setPipeline(renderPipeline);
        render.setBindGroup(0, bindGroup);
        render.draw(6, count);
        render.end();

        device.queue.submit([encoder.finish()]);

        // The exit is a CSS opacity on the canvas rather than a uniform, so settled ink can stay
        // fully opaque in the shader right up until the whole layer leaves.
        if (age > lifetimeMs - fadeMs) {
            canvas.style.opacity = String(Math.max(0, (lifetimeMs - age) / fadeMs));
        }
    });

    instances.set(id, instance);
    canvas.dataset.particlesBackend = 'webgpu';
    return id;
}

export function stop(id) {
    const instance = instances.get(id);
    if (!instance) return;

    instance.stop?.();
    for (const buffer of instance.buffers) {
        try { buffer.destroy(); } catch { /* device already lost */ }
    }
    instance.target.release();
    instance.canvas.style.opacity = '';
    delete instance.canvas.dataset.particlesBackend;
    instances.delete(id);
}

gfx.onTierChanged((tier) => {
    if (tier !== 'full') {
        for (const id of [...instances.keys()]) stop(id);
    }
});
