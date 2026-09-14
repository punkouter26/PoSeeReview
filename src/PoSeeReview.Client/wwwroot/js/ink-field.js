// Wet ink, simulated — the comic develops by soaking into paper rather than by a gradient
// sliding down it.
//
// WHY THIS IS THE SECOND WEBGPU EFFECT, AND WHY IT IS THE RIGHT SECOND ONE.
//
// webgpu-pool.js is deliberately narrow: WebGPU is not a faster WebGL, and porting a fullscreen
// fragment pass to WGSL buys nothing but a second shader language to maintain. It earns its place
// only where COMPUTE changes what the effect can be. particles-gpu.js was the first case — the
// stateless WebGL2 sim fades out mid-air because no particle can know about the floor.
//
// This is the second, and the argument is the same shape. The reveal today is a linear mask on
// the container (app.css), with a noise-threshold boundary added when comic-fx happens to have
// attached. Both are functions of position: whatever the boundary looks like at a point, it looks
// like that because of where the point is. Real ink on paper is not — it is a function of where
// the ink has ALREADY been. It wicks faster along the grain, it pools where two fronts meet, and
// a fibre that has drawn ink keeps drawing it. That is a diffusion field with state, one cell
// reading its neighbours' previous values, and it is exactly what a compute pass is for.
//
// WHAT IT DRAWS. Not a mask — an overlay. The un-developed part of the comic is covered in paper
// colour, and the simulation eats that cover away. Masking would have been the obvious choice and
// is not possible: CSS `mask-image` takes a URL, not a live canvas, so a simulated boundary
// cannot become a mask without a per-frame readback. Covering costs one blend instead.
//
// FAILS TO THE THING THAT ALWAYS SHIPPED. No WebGPU, no device, a shader that will not compile:
// start() returns 0, comic-reveal.js keeps the CSS mask, and the comic develops exactly as it did
// before this file existed. Never read the CSS path as degraded — it is what nearly everyone sees.

import { gfx } from './gfx-core.js';
import { acquireDevice, configureCanvas } from './webgpu-pool.js';

// Simulation grid. Deliberately coarse and TALLER than it is wide, because the front travels
// vertically and the resolution that matters is along the direction of travel. 96x192 is 18k
// cells — a rounding error for a compute pass, and past the point where the blur of the render
// interpolation hides the lattice anyway. A finer grid makes the wicking finer, not better: at
// this scale the fibre detail stops being visible and starts being noise.
const GRID_W = 96;
const GRID_H = 192;
const WORKGROUP = 8;

const SIM_SHADER = /* wgsl */ `
struct Params {
    front      : f32,   // 0..1, how far down the page the wet edge has been pushed
    dt         : f32,
    time       : f32,
    wick       : f32,   // anisotropy: how much more readily ink travels along the grain than across
};

@group(0) @binding(0) var<storage, read>       src    : array<f32>;
@group(0) @binding(1) var<storage, read_write> dst    : array<f32>;
@group(0) @binding(2) var<uniform>             params : Params;

const W : u32 = ${GRID_W}u;
const H : u32 = ${GRID_H}u;

fn idx(x: u32, y: u32) -> u32 { return y * W + x; }

fn sample(x: i32, y: i32) -> f32 {
    // Clamp at the edges rather than wrap. Wrapping would carry ink off the bottom of the panel
    // and back in at the top, which on a comic strip reads as a rendering fault; clamping makes
    // the border behave like the edge of a sheet, which is what it is.
    let cx = u32(clamp(x, 0, i32(W) - 1));
    let cy = u32(clamp(y, 0, i32(H) - 1));
    return src[idx(cx, cy)];
}

// Cheap value noise, used for the paper's fibre. Per-cell and constant in time: the grain of a
// sheet does not move, and animating it turns wicking into shimmer.
fn hash(p: vec2<f32>) -> f32 {
    var q = fract(p * vec2<f32>(123.34, 456.21));
    q = q + vec2<f32>(dot(q, q + 45.32));
    return fract(q.x * q.y);
}

@compute @workgroup_size(${WORKGROUP}, ${WORKGROUP})
fn main(@builtin(global_invocation_id) gid : vec3<u32>) {
    if (gid.x >= W || gid.y >= H) { return; }

    let x = i32(gid.x);
    let y = i32(gid.y);
    let here = sample(x, y);

    // ── Diffusion, anisotropic ──────────────────────────────────────────────────────────
    //
    // The horizontal and vertical neighbours are weighted differently, and that asymmetry IS the
    // paper. Isotropic diffusion produces a circular blot — correct for ink in water, wrong for
    // ink on a sheet, where the fibres run one way and the front travels along them.
    let left  = sample(x - 1, y);
    let right = sample(x + 1, y);
    let up    = sample(x, y - 1);
    let down  = sample(x, y + 1);

    // Fibre density varies per cell, so some columns drink faster than their neighbours. This is
    // what makes the boundary ragged rather than merely soft — a uniform field diffuses into a
    // smooth gradient no matter how long it runs.
    let fibre = 0.55 + hash(vec2<f32>(f32(x) * 0.37, f32(y) * 0.11)) * 0.9;

    let across = (left + right) * 0.5;
    let along  = (up + down) * 0.5;
    let mixed  = mix(across, along, clamp(params.wick, 0.0, 1.0));

    var next = here + (mixed - here) * clamp(params.dt * 6.0 * fibre, 0.0, 0.9);

    // ── The advancing source ────────────────────────────────────────────────────────────
    //
    // Everything above the front is wet. The front is driven from JS (the reveal's own progress),
    // NOT simulated, because the reveal has to finish in a known time — a diffusion front left to
    // find its own pace would take as long as it took, and the comic would still be half covered
    // when the user started scrolling.
    let v = (f32(y) + 0.5) / f32(H);
    if (v < params.front) {
        // Soft shoulder rather than a hard fill, so the newly-wet band is already blending into
        // what the simulation did on previous steps instead of overwriting it with a flat edge.
        let depth = clamp((params.front - v) * 14.0, 0.0, 1.0);
        next = max(next, depth);
    }

    // A tiny bleed ahead of the front, scaled by the fibre. This is the only term that lets ink
    // reach cells the front has not got to yet — which is the whole visual difference between
    // this and a gradient: the boundary runs AHEAD of itself in places.
    if (v >= params.front && v < params.front + 0.06) {
        next = max(next, here + params.dt * fibre * 0.8);
    }

    dst[idx(gid.x, gid.y)] = clamp(next, 0.0, 1.0);
}
`;

const DRAW_SHADER = /* wgsl */ `
struct Params {
    front      : f32,
    dt         : f32,
    time       : f32,
    wick       : f32,
};

@group(0) @binding(0) var<storage, read> field  : array<f32>;
@group(0) @binding(1) var<uniform>       params : Params;
@group(0) @binding(2) var<uniform>       paper  : vec4<f32>;

const W : u32 = ${GRID_W}u;
const H : u32 = ${GRID_H}u;

struct VsOut {
    @builtin(position) pos : vec4<f32>,
    @location(0)       uv  : vec2<f32>,
};

@vertex
fn vs(@builtin(vertex_index) i : u32) -> VsOut {
    // One triangle covering the viewport. No seam, one primitive.
    var p = vec2<f32>(f32((i << 1u) & 2u), f32(i & 2u));
    var out : VsOut;
    out.uv = p;
    out.pos = vec4<f32>(p * 2.0 - 1.0, 0.0, 1.0);
    return out;
}

// Bilinear read of the field. The grid is far coarser than the canvas, so a nearest-neighbour
// lookup would show the 96x192 lattice as visible blocks along the boundary.
fn fieldAt(uv : vec2<f32>) -> f32 {
    let g = vec2<f32>(uv.x * f32(W) - 0.5, uv.y * f32(H) - 0.5);
    let b = floor(g);
    let f = g - b;

    let x0 = u32(clamp(b.x, 0.0, f32(W) - 1.0));
    let y0 = u32(clamp(b.y, 0.0, f32(H) - 1.0));
    let x1 = u32(clamp(b.x + 1.0, 0.0, f32(W) - 1.0));
    let y1 = u32(clamp(b.y + 1.0, 0.0, f32(H) - 1.0));

    let a00 = field[y0 * W + x0];
    let a10 = field[y0 * W + x1];
    let a01 = field[y1 * W + x0];
    let a11 = field[y1 * W + x1];

    return mix(mix(a00, a10, f.x), mix(a01, a11, f.x), f.y);
}

@fragment
fn fs(in : VsOut) -> @location(0) vec4<f32> {
    // uv.y from the vertex shader is 0 at the BOTTOM in clip space; the field is authored top-
    // down, the way the reveal travels. Flipping here rather than in the sim keeps the compute
    // shader's coordinates matching the JS that drives the front.
    let uv = vec2<f32>(in.uv.x, 1.0 - in.uv.y);
    let wet = fieldAt(uv);

    // The cover is what is left of the blank sheet. A soft, slightly off-centre threshold rather
    // than a linear fade: paper does not become gradually transparent, it is either soaked or it
    // is not, and the narrow band between the two is the wet edge the whole effect exists for.
    var cover = 1.0 - smoothstep(0.32, 0.62, wet);

    // A darker line exactly at the boundary — ink pooling at the edge of the wet front, which is
    // what makes a real spreading blot read as wet rather than as a shape getting bigger.
    let edge = (1.0 - smoothstep(0.0, 0.16, abs(wet - 0.47))) * 0.35;

    let tint = paper.rgb * (1.0 - edge);
    return vec4<f32>(tint * cover, cover);
}
`;

const instances = new Map();
let nextId = 1;

/** The paper colour the un-developed region is covered in. Token-driven, so it flips with theme. */
function readPaper() {
    const fallback = [1, 1, 1];
    try {
        const value = getComputedStyle(document.documentElement)
            .getPropertyValue('--color-card').trim();
        const probe = document.createElement('canvas').getContext('2d');
        if (!probe) return fallback;
        probe.fillStyle = '#ffffff';
        probe.fillStyle = value || '#ffffff';
        const match = /^#?([0-9a-f]{6})$/i.exec(probe.fillStyle);
        if (!match) return fallback;
        const int = parseInt(match[1], 16);
        return [((int >> 16) & 255) / 255, ((int >> 8) & 255) / 255, (int & 255) / 255];
    } catch {
        return fallback;
    }
}

/**
 * Starts a wet-ink reveal on an overlay canvas.
 *
 * @param {HTMLCanvasElement} canvas overlay sized to the comic; aria-hidden, pointer-events:none
 * @param {{ wick?: number }} options `wick` 0..1 — how strongly the grain runs vertically
 * @returns {Promise<number>} handle, or 0 if it did not start
 */
export async function start(canvas, options = {}) {
    if (!canvas || !gfx.allows('full')) return 0;

    const acquired = await acquireDevice();
    if (!acquired) return 0;

    const { device, format } = acquired;
    const target = configureCanvas(canvas, device, format, 1.5);
    if (!target) return 0;

    try {
        const cells = GRID_W * GRID_H;
        const bytes = cells * 4;

        // Ping-pong. A compute pass cannot read and write the same storage buffer coherently
        // across workgroups — a cell would read neighbours that this same dispatch had already
        // overwritten, which makes the diffusion rate depend on scheduling order.
        const buffers = [
            device.createBuffer({ size: bytes, usage: GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_DST }),
            device.createBuffer({ size: bytes, usage: GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_DST })
        ];

        // Explicitly zeroed. WebGPU guarantees zeroed buffers, but the reveal starting from
        // anything else would be a fully-developed comic on the first frame.
        const zeros = new Float32Array(cells);
        device.queue.writeBuffer(buffers[0], 0, zeros);
        device.queue.writeBuffer(buffers[1], 0, zeros);

        const paramsBuffer = device.createBuffer({
            size: 16,
            usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST
        });
        const paperBuffer = device.createBuffer({
            size: 16,
            usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST
        });

        const paper = readPaper();
        device.queue.writeBuffer(paperBuffer, 0, new Float32Array([paper[0], paper[1], paper[2], 1]));

        const simModule = device.createShaderModule({ code: SIM_SHADER });
        const drawModule = device.createShaderModule({ code: DRAW_SHADER });

        const simPipeline = device.createComputePipeline({
            layout: 'auto',
            compute: { module: simModule, entryPoint: 'main' }
        });

        const drawPipeline = device.createRenderPipeline({
            layout: 'auto',
            vertex: { module: drawModule, entryPoint: 'vs' },
            fragment: {
                module: drawModule,
                entryPoint: 'fs',
                targets: [{
                    format,
                    // Premultiplied, matching the canvas alphaMode the pool configures. The
                    // fragment shader already multiplies rgb by coverage for this reason.
                    blend: {
                        color: { srcFactor: 'one', dstFactor: 'one-minus-src-alpha' },
                        alpha: { srcFactor: 'one', dstFactor: 'one-minus-src-alpha' }
                    }
                }]
            },
            primitive: { topology: 'triangle-list' }
        });

        // Two bind groups, one per ping-pong direction, built once. Creating them per frame is
        // the single easiest way to make a trivial compute pass allocate more than it computes.
        const simBindGroups = [0, 1].map(i => device.createBindGroup({
            layout: simPipeline.getBindGroupLayout(0),
            entries: [
                { binding: 0, resource: { buffer: buffers[i] } },
                { binding: 1, resource: { buffer: buffers[1 - i] } },
                { binding: 2, resource: { buffer: paramsBuffer } }
            ]
        }));

        const drawBindGroups = [0, 1].map(i => device.createBindGroup({
            layout: drawPipeline.getBindGroupLayout(0),
            entries: [
                { binding: 0, resource: { buffer: buffers[i] } },
                { binding: 1, resource: { buffer: paramsBuffer } },
                { binding: 2, resource: { buffer: paperBuffer } }
            ]
        }));

        const id = nextId++;
        const params = new Float32Array(4);
        let read = 0;
        const startedAt = performance.now();
        let lastFrame = startedAt;

        const instance = {
            canvas,
            device,
            buffers,
            paramsBuffer,
            paperBuffer,
            /** 0..1, pushed from comic-reveal's own progress. */
            front: 0,
            wick: Math.min(1, Math.max(0, options.wick ?? 0.72)),
            stop: null
        };

        instance.stop = gfx.addTask(`ink-field#${id}`, (now) => {
            // Clamped. A throttled tab hands back a huge elapsed, and one diffusion step that
            // large is unstable — the field oscillates and the boundary flickers.
            const dt = Math.min(1 / 30, Math.max(1 / 240, (now - lastFrame) / 1000));
            lastFrame = now;

            params[0] = instance.front;
            params[1] = dt;
            // Elapsed since the reveal began, not since the last frame — lastFrame has already
            // been advanced above, so reading it here would report zero every time.
            params[2] = (now - startedAt) / 1000;
            params[3] = instance.wick;
            device.queue.writeBuffer(paramsBuffer, 0, params);

            target.resize();

            const encoder = device.createCommandEncoder();

            const compute = encoder.beginComputePass();
            compute.setPipeline(simPipeline);
            compute.setBindGroup(0, simBindGroups[read]);
            compute.dispatchWorkgroups(
                Math.ceil(GRID_W / WORKGROUP),
                Math.ceil(GRID_H / WORKGROUP));
            compute.end();

            read = 1 - read;

            const view = target.context.getCurrentTexture().createView();
            const pass = encoder.beginRenderPass({
                colorAttachments: [{
                    view,
                    clearValue: { r: 0, g: 0, b: 0, a: 0 },
                    loadOp: 'clear',
                    storeOp: 'store'
                }]
            });
            pass.setPipeline(drawPipeline);
            pass.setBindGroup(0, drawBindGroups[read]);
            pass.draw(3);
            pass.end();

            device.queue.submit([encoder.finish()]);
        });

        instances.set(id, instance);
        canvas.dataset.inkField = 'on';
        return id;
    } catch (err) {
        console.warn('[ink-field] unavailable; CSS reveal stays', err);
        return 0;
    }
}

/**
 * Pushes the wet edge down the page. 0 is a blank sheet, 1 is fully developed.
 *
 * The front is DRIVEN rather than simulated on purpose: a diffusion front left to find its own
 * pace takes as long as it takes, and the comic would still be half covered when the reader
 * started scrolling. The simulation decides what the boundary LOOKS like; the reveal decides
 * when it is finished.
 */
export function setProgress(id, progress) {
    const instance = instances.get(id);
    if (instance) {
        instance.front = Math.min(1, Math.max(0, progress ?? 0));
    }
}

export function stop(id) {
    const instance = instances.get(id);
    if (!instance) return;

    instance.stop?.();
    // GPU buffers are not collected promptly just because the JS reference went away, and this
    // runs on every comic the user opens.
    for (const buffer of instance.buffers) {
        try { buffer.destroy(); } catch { /* device already lost */ }
    }
    try { instance.paramsBuffer.destroy(); } catch { /* ditto */ }
    try { instance.paperBuffer.destroy(); } catch { /* ditto */ }
    try { delete instance.canvas.dataset.inkField; } catch { /* detached */ }
    instances.delete(id);
}

/** Live fields, for the diagnostics panel. */
export function activeCount() {
    return instances.size;
}

gfx.onTierChanged((tier) => {
    if (tier !== 'full') {
        for (const id of [...instances.keys()]) stop(id);
    }
});
