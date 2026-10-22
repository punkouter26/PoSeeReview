// The score lands, and the page goes off.
//
// THE MOMENT THIS IS FOR. The strangeness ring counts up for 900ms and stops on a number. That
// number is the whole verdict, and a ring that simply stops reads as a progress bar finishing. A
// burst from the ring's centre, scaled by the score, makes a 92 feel like an event and a 30 feel
// like a shrug — the same information, delivered as a physical reaction instead of a digit.
// `fizzle` is the other ending: a place too normal to draw gets a sad little puff from the error
// card's icon that drops, bounces once and dies.
//
// TWO BACKENDS, ONE LOOK.
//   * WebGPU (preferred). Particles live in a storage buffer and never touch the CPU: one compute
//     dispatch spawns them (radial spray, coherent jets, a shock ring and an aftershock for high
//     scores), one compute dispatch per frame integrates curl-noise turbulence + drag + a little
//     gravity, and an instanced render pass draws each one as a soft streak oriented along its
//     velocity. That is what makes 150k+ particles affordable: the per-frame CPU cost is one
//     uniform write and two passes, regardless of count.
//   * WebGL2 (fallback). A CPU simulation of at most 10k particles uploaded as one Float32Array
//     per frame and drawn with the same streak shader, ported to GLSL. Fewer, slightly larger and
//     brighter particles stand in for the density the GPU path gets from sheer count.
//
// BLENDING FOLLOWS THE THEME. On a dark page the sparks are additive light: the shader writes
// colour well above its alpha into a premultiplied canvas, so the compositor adds them to the page
// and dense regions burn toward white. Additive light is invisible on a light page, so there the
// same particles are saturated ink with ordinary premultiplied "over" blending.
//
// COST DECISIONS. 'full' tier only. The overlay canvas exists only for the ~2–3s of the burst and
// is removed with every GPU buffer when it ends — nothing is held between bursts except the
// compiled pipelines (cached per device, because compiling them on the burst frame would hitch the
// exact moment this exists to celebrate; `describe()` warms them). DPR is capped at 1.5. The sim
// clock advances by the scheduler's frame delta, clamped, so a stalled frame does not teleport.

import { gfx, createSurface, compileProgram, getGpuDevice } from './gfx-core.js';

const MAX_DPR = 1.5;
const PEAK_S = 0.1;              // flash peak — when onPeak fires
const GPU_MIN = 4000;            // particles at score 15
const GPU_MAX = 160000;          // particles at score 100
const GL_MIN = 2500;
const GL_MAX = 10000;
const FLOATS = 8;                // pos.xy vel.xy age life seed tag
const UNIFORM_FLOATS = 48;       // 7 param vec4s + 5 colour vec4s
const Z_INDEX = '9990';

// Kinds — the tag float packs kind * 8 + colour index.
const SPRAY = 0, JET = 1, SHOCK = 2, HALO = 3, GLITTER = 4, AFTER = 5;
// Per-kind drag (1/s), gravity weight, turbulence weight. A particle with drag k launched at v0
// travels v0/k, so launch speed is chosen as distance × drag and every kind lands where planned.
const DRAG = [3.1, 2.5, 5.2, 3.6, 1.7, 3.0];
const WEIGHT = [1.0, 0.8, 0.3, 0.6, 0.35, 1.1];
const SWIRL = [1.0, 0.7, 0.4, 0.8, 1.4, 1.0];

const instances = new Map();
let nextId = 1;

const clamp01 = (v) => Math.min(1, Math.max(0, v));

function parseColor(value, fallback) {
    const match = /^#?([0-9a-f]{6})$/i.exec((value ?? '').trim());
    if (!match) return fallback;
    const int = parseInt(match[1], 16);
    return [((int >> 16) & 255) / 255, ((int >> 8) & 255) / 255, (int & 255) / 255];
}

function token(name, fallback) {
    try {
        return parseColor(getComputedStyle(document.documentElement).getPropertyValue(name), fallback);
    } catch {
        return fallback;
    }
}

function isDark() {
    const theme = document.documentElement.dataset.theme;
    if (theme === 'dark') return true;
    if (theme === 'light') return false;
    try {
        return matchMedia('(prefers-color-scheme: dark)').matches;
    } catch {
        return false;
    }
}

/** Element → centre of its box (and its radius, so sparks leave from the rim); {x, y} as given. */
function resolveOrigin(origin) {
    if (origin && typeof origin.getBoundingClientRect === 'function') {
        if (!origin.isConnected) return null;
        const r = origin.getBoundingClientRect();
        return { x: r.left + r.width / 2, y: r.top + r.height / 2, radius: Math.min(r.width, r.height) / 2, el: origin };
    }
    if (origin && Number.isFinite(origin.x) && Number.isFinite(origin.y)) {
        return { x: origin.x, y: origin.y, radius: 0, el: null };
    }
    return null;
}

function forceWebGl(options) {
    if (options.backend === 'webgl2') return true;
    try {
        return /[?&]nogpu\b/.test(location.search);
    } catch {
        return false;
    }
}

function createOverlay() {
    const canvas = document.createElement('canvas');
    canvas.setAttribute('aria-hidden', 'true');
    canvas.dataset.fxParticles = '';
    Object.assign(canvas.style, {
        position: 'fixed', inset: '0', width: '100%', height: '100%',
        pointerEvents: 'none', zIndex: Z_INDEX, contain: 'strict'
    });
    document.body.appendChild(canvas);
    return canvas;
}

/**
 * Everything both backends agree on, derived once from score, viewport and origin. Fractions are
 * cumulative thresholds for the spawn roll: shock, aftershock, halo, jet, glitter, rest = spray.
 */
function makePlan(o, score, options, backend) {
    const e = clamp01((score - 15) / 85);
    const vw = document.documentElement.clientWidth || innerWidth;
    const vh = document.documentElement.clientHeight || innerHeight;
    const reach = Math.min(vw, vh) * (0.12 + 0.3 * Math.pow(e, 1.2)) + 30;
    const dark = isDark();

    const gpuCount = Math.round(GPU_MIN + (GPU_MAX - GPU_MIN) * e * e);
    const count = backend === 'gpu'
        ? gpuCount
        : Math.min(gpuCount, Math.round(GL_MIN + (GL_MAX - GL_MIN) * e));

    // Per-particle intensity falls with count so a 150k burst blooms instead of whiting out.
    // The GL path has fewer particles, so it gets back some of the brightness it loses.
    const density = dark ? Math.sqrt(9000 / count) : Math.sqrt(20000 / count);
    const intensity = dark ? Math.min(1, Math.max(0.18, density)) : Math.min(0.95, Math.max(0.45, density));
    const glBoost = backend === 'gpu' ? 1 : Math.min(1.8, Math.sqrt(gpuCount / count));

    const shock = e > 0.6 ? 0.035 : 0;
    const after = e > 0.75 ? 0.1 : 0;
    const halo = 0.05 + 0.04 * e;
    const jet = 0.18 + 0.14 * e;
    const glitter = 0.06;

    const radius = Number.isFinite(options.radius) ? options.radius : (o.radius ? o.radius * 0.8 : 18);

    return {
        e, reach, dark, count,
        duration: 1.6 + 1.0 * e,
        jets: Math.round(5 + e * 6),
        seed: Math.random(),
        phase: Math.random() * Math.PI * 2,
        radius: Math.min(140, Math.max(0, radius)),
        gravity: 85,
        turbulence: 40 + 90 * e,
        intensity: intensity * glBoost,
        size: (1.0 + 0.35 * e) * (dark ? 1 : 1.2) * (backend === 'gpu' ? 1 : 1.25),
        cuts: [shock, shock + after, shock + after + halo, shock + after + halo + jet, shock + after + halo + jet + glitter],
        afterDelay: 0.27,
        colors: dark
            ? [token('--color-brand', [0.655, 0.545, 0.98]), token('--color-accent', [0.984, 0.749, 0.141]),
               token('--color-brand-alt', [0.96, 0.34, 0.42]), token('--color-danger', [0.97, 0.44, 0.44]),
               token('--color-on-dark', [1, 1, 1])]
            : [token('--color-brand', [0.486, 0.227, 0.929]), token('--color-accent', [0.961, 0.62, 0.043]),
               token('--color-brand-alt', [0.96, 0.34, 0.42]), token('--color-danger', [0.843, 0.145, 0.212]),
               token('--color-accent-dark', [0.706, 0.325, 0.035])],
        origin: { x: o.x, y: o.y }
    };
}

/** Packs the plan + this frame's clock into the shared uniform layout (WGSL Params / GLSL uA..uG). */
function writeUniforms(out, plan, view, offset, time, dt) {
    const c = plan.cuts;
    out.set([
        plan.origin.x, plan.origin.y, view.w, view.h,
        offset.x, offset.y, time, dt,
        plan.e, plan.reach, plan.duration, plan.dark ? 0 : 1,
        plan.jets, plan.seed, plan.count, plan.size,
        plan.radius, plan.gravity, plan.turbulence, plan.intensity,
        c[0], c[1], c[2], c[3],
        c[4], plan.phase, plan.afterDelay, 0
    ]);
    for (let i = 0; i < 5; i++) {
        out.set(plan.colors[i], 28 + i * 4);
        out[28 + i * 4 + 3] = 1;
    }
}

// ── WebGPU ───────────────────────────────────────────────────────────────────────────────

const WGSL_COMMON = /* wgsl */`
struct Particle { pos: vec2f, vel: vec2f, age: f32, life: f32, seed: f32, tag: f32 };
struct Params {
  a: vec4f,   // origin.xy, view.wh (CSS px)
  b: vec4f,   // scroll offset.xy, time, dt
  c: vec4f,   // energy 0..1, reach px, duration s, light 0|1
  d: vec4f,   // jets, seed, count, size
  e: vec4f,   // spawn radius, gravity, turbulence, intensity
  f: vec4f,   // cumulative fractions: shock, aftershock, halo, jet
  g: vec4f,   // cumulative glitter, jet phase, aftershock delay, -
  col: array<vec4f, 5>,   // brand, accent, brand-alt, danger, hot
};
@group(0) @binding(0) var<uniform> U: Params;
const TAU = 6.283185307;
fn pcg(v: u32) -> u32 {
  let s = v * 747796405u + 2891336453u;
  let w = ((s >> ((s >> 28u) + 4u)) ^ s) * 277803737u;
  return (w >> 22u) ^ w;
}
fn unit(v: u32) -> f32 { return f32(v >> 8u) / 16777216.0; }
`;

const WGSL_COMPUTE = WGSL_COMMON + /* wgsl */`
@group(0) @binding(1) var<storage, read_write> P: array<Particle>;

fn rnd(r: ptr<function, u32>) -> f32 { *r = pcg(*r); return unit(*r); }
fn gauss(r: ptr<function, u32>) -> f32 {
  let u1 = max(rnd(r), 1e-6);
  let u2 = rnd(r);
  return sqrt(-2.0 * log(u1)) * cos(TAU * u2);
}
fn drag(k: u32) -> f32 { var a = array<f32, 6>(${DRAG.join(', ')}); return a[k]; }
fn weight(k: u32) -> f32 { var a = array<f32, 6>(${WEIGHT.join(', ')}); return a[k]; }
fn swirl(k: u32) -> f32 { var a = array<f32, 6>(${SWIRL.join(', ')}); return a[k]; }

// Value noise with analytic derivatives (quintic fade): the curl of a scalar potential is
// divergence-free, so the sparks swirl into smoke-like eddies instead of clumping.
fn h2(p: vec2i) -> f32 { return unit(pcg((bitcast<u32>(p.x) * 1597334677u) ^ (bitcast<u32>(p.y) * 3812015801u))); }
fn noised(x: vec2f) -> vec3f {
  let i = vec2i(floor(x));
  let f = fract(x);
  let u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
  let du = 30.0 * f * f * (f * (f - 2.0) + 1.0);
  let a = h2(i);
  let b = h2(i + vec2i(1, 0));
  let c = h2(i + vec2i(0, 1));
  let d = h2(i + vec2i(1, 1));
  let k1 = b - a;
  let k2 = c - a;
  let k4 = a - b - c + d;
  return vec3f(a + k1 * u.x + k2 * u.y + k4 * u.x * u.y, du * vec2f(k1 + k4 * u.y, k2 + k4 * u.x));
}
fn curl(q: vec2f, t: f32) -> vec2f {
  let n1 = noised(q + vec2f(t * 0.35, -t * 0.2));
  let n2 = noised(q * 2.03 + vec2f(-t * 0.5, t * 0.3) + 17.0);
  let g = n1.yz + n2.yz;
  return vec2f(g.y, -g.x);
}

@compute @workgroup_size(64)
fn init(@builtin(global_invocation_id) gid: vec3u) {
  let i = gid.x;
  if (i >= u32(U.d.z)) { return; }
  var r = pcg(i ^ pcg(u32(U.d.y * 4294967.0) + 17u));
  let e = U.c.x;
  let R = U.c.y;
  let D = U.c.z;
  let jets = U.d.x;
  let pick = rnd(&r);
  let r1 = rnd(&r);
  let r2 = rnd(&r);
  let r3 = rnd(&r);
  let g = gauss(&r);
  var angle = r1 * TAU;
  var dist = 0.0;
  var life = D;
  var delay = 0.0;
  var kind = ${SPRAY}u;
  var color = 0u;
  var rim = U.e.x * (0.55 + 0.45 * r3);
  if (pick < U.f.x) {
    kind = ${SHOCK}u; dist = R * 1.22 * (1.0 + 0.012 * g); life = 0.45 + 0.35 * r2;
    color = select(1u, 4u, r3 < 0.6); rim = U.e.x;
  } else if (pick < U.f.y) {
    kind = ${AFTER}u; dist = R * (0.2 + 0.6 * pow(r2, 0.6)); delay = U.g.z + 0.05 * r3;
    life = (D - delay) * (0.45 + 0.55 * r3); color = select(2u, 3u, r3 < 0.5);
  } else if (pick < U.f.z) {
    kind = ${HALO}u; dist = R * 0.6 * (1.0 + 0.03 * g); life = D * (0.4 + 0.2 * r2); color = 2u; rim = U.e.x;
  } else if (pick < U.f.w) {
    kind = ${JET}u;
    let j = floor(r1 * jets);
    angle = (j + 0.35 * unit(pcg(u32(j) + 911u))) * TAU / jets + U.g.y + g * 0.045;
    dist = R * (0.45 + 0.85 * sqrt(r2)) * (1.0 + 0.2 * e);
    life = D * (0.55 + 0.45 * r2);
    delay = 0.025 * r3;
    color = u32(j) % 3u;
    if (e > 0.78 && u32(j) % 4u == 3u) { color = 3u; }
  } else if (pick < U.g.x) {
    kind = ${GLITTER}u; dist = R * (0.25 + 0.85 * r2); life = D * (0.75 + 0.25 * r3); color = select(1u, 4u, r3 < 0.5);
  } else {
    dist = R * (0.1 + 0.9 * pow(r2, 0.55)) * (1.0 + 0.22 * e * sin(angle * jets + U.g.y));
    life = D * (0.4 + 0.6 * sqrt(r3));
    let c = fract(r3 * 7.13 + r2 * 3.7);
    color = select(select(2u, 1u, c < 0.72), 0u, c < 0.42);
    if (e > 0.76 && fract(c * 11.0) < (e - 0.76) * 1.6) { color = 3u; }
  }
  let dir = vec2f(cos(angle), sin(angle));
  var p: Particle;
  p.pos = U.a.xy + dir * rim;
  p.vel = dir * dist * drag(kind);
  p.age = -delay;
  p.life = max(life, 0.2);
  p.seed = fract(r1 * 13.7 + r2 * 3.1 + pick);
  p.tag = f32(kind * 8u + color);
  P[i] = p;
}

@compute @workgroup_size(64)
fn advance(@builtin(global_invocation_id) gid: vec3u) {
  let i = gid.x;
  let dt = U.b.w;
  if (i >= u32(U.d.z) || dt <= 0.0) { return; }
  var p = P[i];
  p.age += dt;
  if (p.age < 0.0 || p.age >= p.life) { P[i] = p; return; }
  let kind = u32(p.tag) >> 3u;
  p.vel *= exp(-drag(kind) * dt);
  p.vel.y += U.e.y * weight(kind) * dt;
  let q = p.pos / 160.0 + vec2f(U.d.y * 31.0, U.d.y * 17.0);
  let amount = U.e.z * swirl(kind) * smoothstep(0.03, 0.45, p.age);
  p.pos += (p.vel + curl(q, U.b.z) * amount) * dt;
  P[i] = p;
}
`;

const WGSL_RENDER = WGSL_COMMON + /* wgsl */`
@group(0) @binding(1) var<storage, read> P: array<Particle>;
struct VOut {
  @builtin(position) pos: vec4f,
  @location(0) local: vec2f,                       // px: along velocity, across it
  @location(1) @interpolate(flat) shape: vec2f,    // half segment length, half width
  @location(2) color: vec4f,
};
fn toClip(px: vec2f) -> vec4f { return vec4f(px.x / U.a.z * 2.0 - 1.0, 1.0 - px.y / U.a.w * 2.0, 0.0, 1.0); }
fn corner(vi: u32) -> vec2f {
  var c = array<vec2f, 6>(vec2f(-1.0, -1.0), vec2f(1.0, -1.0), vec2f(-1.0, 1.0),
                          vec2f(-1.0, 1.0), vec2f(1.0, -1.0), vec2f(1.0, 1.0));
  return c[vi];
}

@vertex
fn vsParticle(@builtin(vertex_index) vi: u32, @builtin(instance_index) ii: u32) -> VOut {
  var o: VOut;
  o.pos = vec4f(2.0, 2.0, 2.0, 1.0);   // dead or waiting: every vertex identical → culled
  o.local = vec2f(0.0);
  o.shape = vec2f(1.0);
  o.color = vec4f(0.0);
  let p = P[ii];
  if (p.age <= 0.0 || p.age >= p.life) { return o; }
  let t = p.age / p.life;
  let kind = u32(p.tag) >> 3u;
  let ci = u32(p.tag) & 7u;
  let speed = length(p.vel);
  let dir = select(vec2f(1.0, 0.0), p.vel / max(speed, 1e-4), speed > 1e-3);
  let across = vec2f(-dir.y, dir.x);
  var hw = (0.7 + 1.0 * fract(p.seed * 7.31)) * U.d.w;
  if (kind == ${SHOCK}u) { hw *= 1.35; }
  if (kind == ${GLITTER}u) { hw *= 0.8; }
  let hl = min(speed * 0.011, 30.0);
  let pad = hw * 2.5;
  let c = corner(vi);
  let local = vec2f(c.x * (hl + pad), c.y * pad);
  o.pos = toClip(p.pos + U.b.xy + dir * local.x + across * local.y);
  o.local = local;
  o.shape = vec2f(hl, hw);
  let hot = exp(-p.age * 9.0) * (1.0 - U.c.w);
  let rgb = mix(U.col[ci].rgb, U.col[4].rgb, hot * 0.7);
  // Early on the cloud is packed and every spark overlaps hundreds of others; dim them until it spreads.
  var a = pow(1.0 - t, 1.5) * smoothstep(0.0, 0.035, p.age) * U.e.w * mix(0.12, 1.4, smoothstep(0.0, 0.9, p.age));
  if (kind == ${GLITTER}u) { a *= 0.3 + 0.9 * pow(0.5 + 0.5 * sin(U.b.z * 26.0 + p.seed * 80.0), 4.0); }

  o.color = vec4f(rgb, a);
  return o;
}

@fragment
fn fsParticle(i: VOut) -> @location(0) vec4f {
  let d = length(vec2f(max(abs(i.local.x) - i.shape.x, 0.0), i.local.y)) / i.shape.y;
  var a = exp(-d * d * 0.9) * i.color.a;
  a *= mix(0.35, 1.0, smoothstep(-1.0, 1.0, i.local.x / (i.shape.x + i.shape.y * 2.5)));
  if (U.c.w > 0.5) { a = min(a, 1.0); return vec4f(i.color.rgb * a, a); }
  // Dark: colour far above alpha — the premultiplied compositor adds it to the page as light.
  return vec4f(i.color.rgb * a, a * 0.06);
}

@vertex
fn vsFx(@builtin(vertex_index) vi: u32) -> VOut {
  var o: VOut;
  let half = U.c.y * 1.45 + 40.0;
  let local = corner(vi) * half;
  o.pos = toClip(U.a.xy + U.b.xy + local);
  o.local = local;
  o.shape = vec2f(half, 0.0);
  o.color = vec4f(0.0);
  return o;
}

// The flash and the analytic shock fronts: crisp rings no particle cloud can draw.
@fragment
fn fsFx(i: VOut) -> @location(0) vec4f {
  let t = U.b.z;
  let e = U.c.x;
  let R = U.c.y;
  let r = length(i.local);
  let flash = smoothstep(0.0, 0.07, t) * exp(-max(t - 0.07, 0.0) * 6.0) * (0.35 + 0.45 * e);
  let core = exp(-pow(r / (R * 0.14 + 24.0), 2.0)) * flash;
  let th = 2.0 + 18.0 * t;
  let rr = R * 1.25 * (1.0 - exp(-t * 7.0));
  let ring = exp(-pow((r - rr) / th, 2.0)) * pow(max(1.0 - t / 0.75, 0.0), 2.0) * smoothstep(0.35, 0.7, e);
  let t2 = max(t - 0.12, 0.0);
  let rr2 = R * 0.8 * (1.0 - exp(-t2 * 5.0));
  let ring2 = exp(-pow((r - rr2) / (th * 1.6), 2.0)) * pow(max(1.0 - t2 / 0.9, 0.0), 2.0)
            * step(0.12, t) * smoothstep(0.75, 0.95, e) * 0.6;
  if (U.c.w > 0.5) {
    let aRing = ring * 0.7;
    let aRing2 = ring2 * 0.6;
    let aCore = core * 0.4;
    let a = min(aRing + aRing2 + aCore, 1.0);
    let rgb = U.col[0].rgb * aRing + U.col[2].rgb * aRing2 + U.col[1].rgb * aCore;
    return vec4f(min(rgb, vec3f(a)), a);
  }
  let coreRgb = mix(U.col[4].rgb, U.col[1].rgb, smoothstep(0.0, R * 0.25, r));
  let rgb = U.col[4].rgb * ring * 0.9 + U.col[2].rgb * ring2 + coreRgb * core * 1.2;
  return vec4f(rgb, (ring + ring2 + core) * 0.15);
}
`;

const gpuKits = new WeakMap();
const watchedDevices = new WeakSet();

/** Compiled pipelines for a device, built once and async so the burst frame never compiles. */
function gpuKit(device) {
    let promise = gpuKits.get(device);
    if (!promise) {
        promise = buildGpuKit(device).catch((err) => {
            console.warn('[particles] WebGPU pipelines unavailable; using WebGL2', err?.message ?? err);
            return null;
        });
        gpuKits.set(device, promise);
    }
    if (!watchedDevices.has(device)) {
        watchedDevices.add(device);
        device.lost.then(() => {
            for (const [id, instance] of instances) {
                if (instance.device === device) stop(id);
            }
        }).catch(() => {});
    }
    return promise;
}

async function buildGpuKit(device) {
    const format = navigator.gpu.getPreferredCanvasFormat();
    const S = GPUShaderStage;
    device.pushErrorScope('validation');
    const computeModule = device.createShaderModule({ code: WGSL_COMPUTE });
    const renderModule = device.createShaderModule({ code: WGSL_RENDER });
    const computeLayout = device.createBindGroupLayout({ entries: [
        { binding: 0, visibility: S.COMPUTE, buffer: { type: 'uniform' } },
        { binding: 1, visibility: S.COMPUTE, buffer: { type: 'storage' } }
    ] });
    const renderLayout = device.createBindGroupLayout({ entries: [
        { binding: 0, visibility: S.VERTEX | S.FRAGMENT, buffer: { type: 'uniform' } },
        { binding: 1, visibility: S.VERTEX, buffer: { type: 'read-only-storage' } }
    ] });
    const computePipelineLayout = device.createPipelineLayout({ bindGroupLayouts: [computeLayout] });
    const renderPipelineLayout = device.createPipelineLayout({ bindGroupLayouts: [renderLayout] });

    const add = { srcFactor: 'one', dstFactor: 'one', operation: 'add' };
    const over = { srcFactor: 'one', dstFactor: 'one-minus-src-alpha', operation: 'add' };
    const compute = (entryPoint) => device.createComputePipelineAsync({
        layout: computePipelineLayout, compute: { module: computeModule, entryPoint }
    });
    const render = (vs, fs, blend) => device.createRenderPipelineAsync({
        layout: renderPipelineLayout,
        vertex: { module: renderModule, entryPoint: vs },
        fragment: { module: renderModule, entryPoint: fs, targets: [{ format, blend: { color: blend, alpha: blend } }] },
        primitive: { topology: 'triangle-list' }
    });

    try {
        const [init, step, sparkDark, sparkLight, fxDark, fxLight] = await Promise.all([
            compute('init'), compute('advance'),
            render('vsParticle', 'fsParticle', add), render('vsParticle', 'fsParticle', over),
            render('vsFx', 'fsFx', add), render('vsFx', 'fsFx', over)
        ]);
        const error = await device.popErrorScope();
        if (error) throw new Error(error.message);
        return { format, computeLayout, renderLayout, init, step, sparkDark, sparkLight, fxDark, fxLight };
    } catch (err) {
        for (const module of [computeModule, renderModule]) {
            try {
                for (const m of (await module.getCompilationInfo()).messages) {
                    if (m.type === 'error') console.warn(`[particles] WGSL ${m.lineNum}:${m.linePos} ${m.message}`);
                }
            } catch { /* diagnostics only */ }
        }
        throw err;
    }
}

function createGpuRenderer(device, kit, plan) {
    const canvas = createOverlay();
    let context = null;
    const buffers = [];
    try {
        context = canvas.getContext('webgpu');
        if (!context) throw new Error('no webgpu context');
        context.configure({ device, format: kit.format, alphaMode: 'premultiplied' });

        const particles = device.createBuffer({ size: plan.count * FLOATS * 4, usage: GPUBufferUsage.STORAGE });
        const uniforms = device.createBuffer({ size: UNIFORM_FLOATS * 4, usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST });
        buffers.push(particles, uniforms);
        const entries = [{ binding: 0, resource: { buffer: uniforms } }, { binding: 1, resource: { buffer: particles } }];
        const computeGroup = device.createBindGroup({ layout: kit.computeLayout, entries });
        const renderGroup = device.createBindGroup({ layout: kit.renderLayout, entries });
        const sparks = plan.dark ? kit.sparkDark : kit.sparkLight;
        const fx = plan.dark ? kit.fxDark : kit.fxLight;
        const workgroups = Math.ceil(plan.count / 64);
        const data = new Float32Array(UNIFORM_FLOATS);

        const view = () => ({ w: canvas.clientWidth || innerWidth, h: canvas.clientHeight || innerHeight });

        writeUniforms(data, plan, view(), { x: 0, y: 0 }, 0, 0);
        device.queue.writeBuffer(uniforms, 0, data);
        const encoder = device.createCommandEncoder();
        const pass = encoder.beginComputePass();
        pass.setPipeline(kit.init);
        pass.setBindGroup(0, computeGroup);
        pass.dispatchWorkgroups(workgroups);
        pass.end();
        device.queue.submit([encoder.finish()]);

        return {
            canvas,
            device,
            frame(time, dt, offset) {
                const v = view();
                const dpr = Math.min(devicePixelRatio || 1, MAX_DPR);
                const w = Math.max(1, Math.round(v.w * dpr));
                const h = Math.max(1, Math.round(v.h * dpr));
                if (canvas.width !== w || canvas.height !== h) {
                    canvas.width = w;
                    canvas.height = h;
                }
                writeUniforms(data, plan, v, offset, time, dt);
                device.queue.writeBuffer(uniforms, 0, data);
                const enc = device.createCommandEncoder();
                const cp = enc.beginComputePass();
                cp.setPipeline(kit.step);
                cp.setBindGroup(0, computeGroup);
                cp.dispatchWorkgroups(workgroups);
                cp.end();
                const rp = enc.beginRenderPass({ colorAttachments: [{
                    view: context.getCurrentTexture().createView(),
                    clearValue: { r: 0, g: 0, b: 0, a: 0 }, loadOp: 'clear', storeOp: 'store'
                }] });
                rp.setBindGroup(0, renderGroup);
                rp.setPipeline(fx);
                rp.draw(6);
                rp.setPipeline(sparks);
                rp.draw(6, plan.count);
                rp.end();
                device.queue.submit([enc.finish()]);
            },
            destroy() {
                // Submitted work that still reads these finishes first; the memory goes now.
                for (const buffer of buffers) {
                    try { buffer.destroy(); } catch { /* already gone */ }
                }
                try { context.unconfigure(); } catch { /* already gone */ }
                canvas.remove();
            }
        };
    } catch (err) {
        for (const buffer of buffers) {
            try { buffer.destroy(); } catch { /* already gone */ }
        }
        try { context?.unconfigure(); } catch { /* already gone */ }
        canvas.remove();
        console.warn('[particles] WebGPU burst failed; using WebGL2', err?.message ?? err);
        return null;
    }
}

// ── WebGL2 fallback ──────────────────────────────────────────────────────────────────────

const GLSL_UNIFORMS = `
uniform vec4 uA, uB, uC, uD, uE;
uniform vec3 uCol[5];
vec4 toClip(vec2 px) { return vec4(px.x / uA.z * 2.0 - 1.0, 1.0 - px.y / uA.w * 2.0, 0.0, 1.0); }
const vec2 CORNERS[6] = vec2[6](vec2(-1.0, -1.0), vec2(1.0, -1.0), vec2(-1.0, 1.0),
                                vec2(-1.0, 1.0), vec2(1.0, -1.0), vec2(1.0, 1.0));
`;

const GL_SPARK_VS = `#version 300 es
precision highp float;
layout(location = 0) in vec4 aPosVel;
layout(location = 1) in vec4 aMeta;   // age, life, seed, tag
${GLSL_UNIFORMS}
out vec2 vLocal;
flat out vec2 vShape;
out vec4 vColor;
void main() {
    gl_Position = vec4(2.0, 2.0, 2.0, 1.0);
    vLocal = vec2(0.0); vShape = vec2(1.0); vColor = vec4(0.0);
    float age = aMeta.x, life = aMeta.y, seed = aMeta.z;
    if (age <= 0.0 || age >= life) return;
    int tag = int(aMeta.w + 0.5);
    int kind = tag >> 3;
    int ci = tag & 7;
    vec2 vel = aPosVel.zw;
    float speed = length(vel);
    vec2 dir = speed > 1e-3 ? vel / speed : vec2(1.0, 0.0);
    vec2 across = vec2(-dir.y, dir.x);
    float hw = (0.7 + 1.0 * fract(seed * 7.31)) * uD.w;
    if (kind == ${SHOCK}) hw *= 1.35;
    if (kind == ${GLITTER}) hw *= 0.8;
    float hl = min(speed * 0.011, 30.0);
    float pad = hw * 2.5;
    vec2 c = CORNERS[gl_VertexID];
    vec2 local = vec2(c.x * (hl + pad), c.y * pad);
    gl_Position = toClip(aPosVel.xy + uB.xy + dir * local.x + across * local.y);
    vLocal = local;
    vShape = vec2(hl, hw);
    float hot = exp(-age * 9.0) * (1.0 - uC.w);
    vec3 rgb = mix(uCol[ci], uCol[4], hot * 0.7);
    float a = pow(1.0 - age / life, 1.5) * smoothstep(0.0, 0.035, age) * uE.w * mix(0.12, 1.4, smoothstep(0.0, 0.9, age));
    if (kind == ${GLITTER}) a *= 0.3 + 0.9 * pow(0.5 + 0.5 * sin(uB.z * 26.0 + seed * 80.0), 4.0);

    vColor = vec4(rgb, a);
}`;

const GL_SPARK_FS = `#version 300 es
precision highp float;
uniform vec4 uC;
in vec2 vLocal;
flat in vec2 vShape;
in vec4 vColor;
out vec4 fragColor;
void main() {
    float d = length(vec2(max(abs(vLocal.x) - vShape.x, 0.0), vLocal.y)) / vShape.y;
    float a = exp(-d * d * 0.9) * vColor.a;
    a *= mix(0.35, 1.0, smoothstep(-1.0, 1.0, vLocal.x / (vShape.x + vShape.y * 2.5)));
    if (uC.w > 0.5) { a = min(a, 1.0); fragColor = vec4(vColor.rgb * a, a); }
    else fragColor = vec4(vColor.rgb * a, a * 0.06);
}`;

const GL_FX_VS = `#version 300 es
precision highp float;
${GLSL_UNIFORMS}
out vec2 vLocal;
void main() {
    float half_ = uC.y * 1.45 + 40.0;
    vLocal = CORNERS[gl_VertexID] * half_;
    gl_Position = toClip(uA.xy + uB.xy + vLocal);
}`;

const GL_FX_FS = `#version 300 es
precision highp float;
uniform vec4 uB, uC;
uniform vec3 uCol[5];
in vec2 vLocal;
out vec4 fragColor;
void main() {
    float t = uB.z, e = uC.x, R = uC.y;
    float r = length(vLocal);
    float flash = smoothstep(0.0, 0.07, t) * exp(-max(t - 0.07, 0.0) * 6.0) * (0.35 + 0.45 * e);
    float core = exp(-pow(r / (R * 0.14 + 24.0), 2.0)) * flash;
    float th = 2.0 + 18.0 * t;
    float rr = R * 1.25 * (1.0 - exp(-t * 7.0));
    float ring = exp(-pow((r - rr) / th, 2.0)) * pow(max(1.0 - t / 0.75, 0.0), 2.0) * smoothstep(0.35, 0.7, e);
    float t2 = max(t - 0.12, 0.0);
    float rr2 = R * 0.8 * (1.0 - exp(-t2 * 5.0));
    float ring2 = exp(-pow((r - rr2) / (th * 1.6), 2.0)) * pow(max(1.0 - t2 / 0.9, 0.0), 2.0)
                * step(0.12, t) * smoothstep(0.75, 0.95, e) * 0.6;
    if (uC.w > 0.5) {
        float aRing = ring * 0.7, aRing2 = ring2 * 0.6, aCore = core * 0.4;
        float a = min(aRing + aRing2 + aCore, 1.0);
        vec3 rgb = uCol[0] * aRing + uCol[2] * aRing2 + uCol[1] * aCore;
        fragColor = vec4(min(rgb, vec3(a)), a);
    } else {
        vec3 coreRgb = mix(uCol[4], uCol[1], smoothstep(0.0, R * 0.25, r));
        vec3 rgb = uCol[4] * ring * 0.9 + uCol[2] * ring2 + coreRgb * core * 1.2;
        fragColor = vec4(rgb, (ring + ring2 + core) * 0.15);
    }
}`;

/** CPU twin of the WGSL `init` entry point — same kinds, same distributions. */
function spawnCpu(plan) {
    const { count, e, reach: R, duration: D, jets, cuts, phase, radius } = plan;
    const data = new Float32Array(count * FLOATS);
    const gauss = () => Math.sqrt(-2 * Math.log(Math.max(Math.random(), 1e-6))) * Math.cos(Math.PI * 2 * Math.random());
    const jetJitter = Array.from({ length: jets }, () => Math.random());
    for (let i = 0; i < count; i++) {
        const pick = Math.random(), r1 = Math.random(), r2 = Math.random(), r3 = Math.random(), g = gauss();
        let angle = r1 * Math.PI * 2, dist, life = D, delay = 0, kind = SPRAY, color = 0;
        let from = radius * (0.55 + 0.45 * r3);
        if (pick < cuts[0]) {
            kind = SHOCK; dist = R * 1.22 * (1 + 0.012 * g); life = 0.45 + 0.35 * r2; color = r3 < 0.6 ? 4 : 1; from = radius;
        } else if (pick < cuts[1]) {
            kind = AFTER; dist = R * (0.2 + 0.6 * Math.pow(r2, 0.6)); delay = plan.afterDelay + 0.05 * r3;
            life = (D - delay) * (0.45 + 0.55 * r3); color = r3 < 0.5 ? 3 : 2;
        } else if (pick < cuts[2]) {
            kind = HALO; dist = R * 0.6 * (1 + 0.03 * g); life = D * (0.4 + 0.2 * r2); color = 2; from = radius;
        } else if (pick < cuts[3]) {
            kind = JET;
            const j = Math.floor(r1 * jets);
            angle = (j + 0.35 * jetJitter[j]) * Math.PI * 2 / jets + phase + g * 0.045;
            dist = R * (0.45 + 0.85 * Math.sqrt(r2)) * (1 + 0.2 * e);
            life = D * (0.55 + 0.45 * r2);
            delay = 0.025 * r3;
            color = e > 0.78 && j % 4 === 3 ? 3 : j % 3;
        } else if (pick < cuts[4]) {
            kind = GLITTER; dist = R * (0.25 + 0.85 * r2); life = D * (0.75 + 0.25 * r3); color = r3 < 0.5 ? 4 : 1;
        } else {
            dist = R * (0.1 + 0.9 * Math.pow(r2, 0.55)) * (1 + 0.22 * e * Math.sin(angle * jets + phase));
            life = D * (0.4 + 0.6 * Math.sqrt(r3));
            const c = (r3 * 7.13 + r2 * 3.7) % 1;
            color = c < 0.42 ? 0 : c < 0.72 ? 1 : 2;
            if (e > 0.76 && (c * 11) % 1 < (e - 0.76) * 1.6) color = 3;
        }
        const dx = Math.cos(angle), dy = Math.sin(angle), o = i * FLOATS;
        data[o] = plan.origin.x + dx * from;
        data[o + 1] = plan.origin.y + dy * from;
        data[o + 2] = dx * dist * DRAG[kind];
        data[o + 3] = dy * dist * DRAG[kind];
        data[o + 4] = -delay;
        data[o + 5] = Math.max(life, 0.2);
        data[o + 6] = Math.random();
        data[o + 7] = kind * 8 + color;
    }
    return data;
}

/**
 * CPU twin of the WGSL `step`. The curl field here is a sum of sines rather than lattice noise:
 * at 10k particles a hash per lattice corner is the dominant JS cost, and the eye cannot tell
 * the two apart on sparks that live two seconds.
 */
function stepCpu(data, plan, time, dt) {
    if (dt <= 0) return;
    const { count, gravity, turbulence } = plan;
    const sx = plan.seed * 31, sy = plan.seed * 17;
    for (let o = 0; o < count * FLOATS; o += FLOATS) {
        const age = data[o + 4] + dt;
        data[o + 4] = age;
        if (age < 0 || age >= data[o + 5]) continue;
        const kind = data[o + 7] >> 3;
        const k = Math.exp(-DRAG[kind] * dt);
        const vx = data[o + 2] * k;
        const vy = data[o + 3] * k + gravity * WEIGHT[kind] * dt;
        data[o + 2] = vx;
        data[o + 3] = vy;
        const x = data[o] / 160 + sx, y = data[o + 1] / 160 + sy;
        const A = 1.7 * x + 0.6 * time, B = 1.3 * y - 0.4 * time;
        const C = 2.3 * (0.8 * x - 0.6 * y) + 1.1 * time, E = 1.9 * (0.5 * x + 0.85 * y) - 0.7 * time;
        const ddx = 1.7 * Math.cos(A) * Math.cos(B) + 0.92 * Math.cos(C) + 0.475 * Math.cos(E);
        const ddy = -1.3 * Math.sin(A) * Math.sin(B) - 0.69 * Math.cos(C) + 0.81 * Math.cos(E);
        const s = turbulence * SWIRL[kind] * 0.45 * smoothstep(0.03, 0.45, age);
        data[o] += (vx + ddy * s) * dt;
        data[o + 1] += (vy - ddx * s) * dt;
    }
}

function smoothstep(a, b, x) {
    const t = clamp01((x - a) / (b - a));
    return t * t * (3 - 2 * t);
}

function createGlRenderer(plan, onLost) {
    const canvas = createOverlay();
    const surface = createSurface(canvas, { maxDpr: MAX_DPR });
    if (!surface) {
        canvas.remove();
        return null;
    }
    const gl = surface.gl;
    const lost = (event) => { event.preventDefault(); onLost(); };
    canvas.addEventListener('webglcontextlost', lost);
    const objects = { programs: [], vaos: [], buffers: [] };

    const teardown = () => {
        canvas.removeEventListener('webglcontextlost', lost);
        try {
            if (!gl.isContextLost()) {
                objects.programs.forEach((p) => gl.deleteProgram(p));
                objects.vaos.forEach((v) => gl.deleteVertexArray(v));
                objects.buffers.forEach((b) => gl.deleteBuffer(b));
            }
        } catch { /* context already gone */ }
        surface.release();
        canvas.remove();
    };

    try {
        const spark = compileProgram(gl, GL_SPARK_VS, GL_SPARK_FS);
        const fx = compileProgram(gl, GL_FX_VS, GL_FX_FS);
        objects.programs.push(spark, fx);
        const locate = (program) => Object.fromEntries(['uA', 'uB', 'uC', 'uD', 'uE', 'uCol']
            .map((name) => [name, gl.getUniformLocation(program, name)]));
        const sparkU = locate(spark);
        const fxU = locate(fx);

        const data = spawnCpu(plan);
        const vao = gl.createVertexArray();
        const fxVao = gl.createVertexArray();
        const vbo = gl.createBuffer();
        objects.vaos.push(vao, fxVao);
        objects.buffers.push(vbo);
        gl.bindVertexArray(vao);
        gl.bindBuffer(gl.ARRAY_BUFFER, vbo);
        gl.bufferData(gl.ARRAY_BUFFER, data.byteLength, gl.DYNAMIC_DRAW);
        gl.enableVertexAttribArray(0);
        gl.vertexAttribPointer(0, 4, gl.FLOAT, false, FLOATS * 4, 0);
        gl.vertexAttribDivisor(0, 1);
        gl.enableVertexAttribArray(1);
        gl.vertexAttribPointer(1, 4, gl.FLOAT, false, FLOATS * 4, 16);
        gl.vertexAttribDivisor(1, 1);
        gl.bindVertexArray(null);

        const u = new Float32Array(UNIFORM_FLOATS);
        const colors = new Float32Array(15);
        plan.colors.forEach((c, i) => colors.set(c, i * 3));
        const bind = (loc) => {
            if (loc.uA) gl.uniform4fv(loc.uA, u.subarray(0, 4));
            if (loc.uB) gl.uniform4fv(loc.uB, u.subarray(4, 8));
            if (loc.uC) gl.uniform4fv(loc.uC, u.subarray(8, 12));
            if (loc.uD) gl.uniform4fv(loc.uD, u.subarray(12, 16));
            if (loc.uE) gl.uniform4fv(loc.uE, u.subarray(16, 20));
            if (loc.uCol) gl.uniform3fv(loc.uCol, colors);
        };

        return {
            canvas,
            device: null,
            frame(time, dt, offset) {
                stepCpu(data, plan, time, dt);
                if (!surface.beginFrame()) return;
                const view = { w: canvas.clientWidth || innerWidth, h: canvas.clientHeight || innerHeight };
                writeUniforms(u, plan, view, offset, time, dt);
                gl.clearColor(0, 0, 0, 0);
                gl.clear(gl.COLOR_BUFFER_BIT);
                gl.enable(gl.BLEND);
                if (plan.dark) gl.blendFunc(gl.ONE, gl.ONE);
                else gl.blendFunc(gl.ONE, gl.ONE_MINUS_SRC_ALPHA);

                gl.useProgram(fx);
                bind(fxU);
                gl.bindVertexArray(fxVao);
                gl.drawArrays(gl.TRIANGLES, 0, 6);

                gl.useProgram(spark);
                bind(sparkU);
                gl.bindVertexArray(vao);
                gl.bindBuffer(gl.ARRAY_BUFFER, vbo);
                gl.bufferSubData(gl.ARRAY_BUFFER, 0, data);
                gl.drawArraysInstanced(gl.TRIANGLES, 0, 6, plan.count);
                gl.bindVertexArray(null);
            },
            destroy: teardown
        };
    } catch (err) {
        console.warn('[particles] WebGL2 burst failed', err?.message ?? err);
        teardown();
        return null;
    }
}

// ── Public API ───────────────────────────────────────────────────────────────────────────

/**
 * Fires the score burst from `origin` (an Element — its centre — or {x, y} client coords).
 * Full tier only. Resolves to a handle, or 0 when nothing ran. `options.score` 0–100 scales
 * everything; `options.onPeak()` fires at the flash peak (~100ms in) — the moment for a backdrop
 * ripple and a thump; `options.radius` overrides the spawn rim; `options.backend: 'webgl2'` (or
 * `?nogpu` in the URL) forces the fallback.
 */
export async function burst(origin, options = {}) {
    try {
        if (!gfx.allows('full') || !resolveOrigin(origin)) return 0;
        const score = Math.min(100, Math.max(0, Number(options.score) || 0));

        let device = null;
        let kit = null;
        if (!forceWebGl(options)) {
            device = await getGpuDevice();
            if (device) kit = await gpuKit(device);
        }

        // The page may have moved on while the adapter was being asked for.
        if (!gfx.allows('full') || document.hidden) return 0;
        const o = resolveOrigin(origin);
        if (!o) return 0;

        const id = nextId++;
        let plan = makePlan(o, score, options, kit ? 'gpu' : 'gl');
        let renderer = kit ? createGpuRenderer(device, kit, plan) : null;
        if (!renderer) {
            if (kit) plan = makePlan(o, score, options, 'gl');
            renderer = createGlRenderer(plan, () => stop(id));
        }
        if (!renderer) return 0;

        const duration = plan.duration + 0.05;
        const scroll0 = { x: scrollX, y: scrollY };
        const onPeak = typeof options.onPeak === 'function' ? options.onPeak : null;
        const instance = { device: renderer.device, renderer, time: 0, peaked: false, needs: 'full', stop: null };

        instance.stop = gfx.addTask(`particles#${id}`, (now, elapsed) => {
            try {
                if (!renderer.canvas.isConnected || (o.el && !o.el.isConnected)) {
                    stop(id);
                    return;
                }
                const dt = Math.min(elapsed, 50) / 1000;
                instance.time += dt;
                // Particles are page-anchored: scrolling mid-burst carries them with the ring.
                renderer.frame(instance.time, dt, { x: scroll0.x - scrollX, y: scroll0.y - scrollY });
                if (!instance.peaked && instance.time >= PEAK_S) {
                    instance.peaked = true;
                    try { onPeak?.(); } catch { /* a cue failing must not stop the burst */ }
                }
                if (instance.time >= duration) stop(id);
            } catch (err) {
                console.warn('[particles] burst frame failed', err?.message ?? err);
                stop(id);
            }
        });
        instances.set(id, instance);
        return id;
    } catch (err) {
        console.warn('[particles] burst failed', err?.message ?? err);
        return 0;
    }
}

/**
 * The sad version, for a place rejected as too normal: a few dozen grey motes pop weakly out of
 * `origin`, drop, bounce once on an invisible floor ~120px below and fade. A small 2D canvas —
 * no GPU — so it runs on the lite tier too. Returns a handle, or 0.
 */
export function fizzle(origin) {
    let canvas = null;
    try {
        if (!gfx.allows('lite')) return 0;
        const o = resolveOrigin(origin);
        if (!o) return 0;

        const W = 300, H = 250, OX = 150, OY = 96, FLOOR = OY + 120;
        const dpr = Math.min(devicePixelRatio || 1, 2);
        canvas = document.createElement('canvas');
        canvas.setAttribute('aria-hidden', 'true');
        canvas.dataset.fxParticles = 'fizzle';
        canvas.width = W * dpr;
        canvas.height = H * dpr;
        Object.assign(canvas.style, {
            position: 'fixed', left: `${o.x - OX}px`, top: `${o.y - OY}px`, width: `${W}px`, height: `${H}px`,
            pointerEvents: 'none', zIndex: Z_INDEX
        });
        const ctx = canvas.getContext('2d');
        if (!ctx) {
            canvas = null;
            return 0;
        }
        document.body.appendChild(canvas);

        const hex = (c) => `rgb(${c.map((v) => Math.round(v * 255)).join(',')})`;
        const shades = [hex(token('--color-text-muted', [0.4, 0.44, 0.44])), hex(token('--color-border-strong', [0.56, 0.56, 0.71]))];
        const n = 64;
        const motes = Array.from({ length: n }, (_, i) => {
            const angle = -Math.PI / 2 + (Math.random() + Math.random() - 1) * 0.9;
            const speed = 80 + Math.random() * 150;
            return {
                x: OX + (Math.random() - 0.5) * 10, y: OY + (Math.random() - 0.5) * 6,
                vx: Math.cos(angle) * speed * 0.8, vy: Math.sin(angle) * speed,
                life: 1.3 + Math.random() * 0.6, size: 1.2 + Math.random() * 2.2,
                color: shades[i & 1], bounced: false
            };
        });
        const end = Math.max(...motes.map((m) => m.life));
        const scroll0 = { x: scrollX, y: scrollY };
        const id = nextId++;
        const instance = { device: null, renderer: { destroy: () => canvas.remove() }, needs: 'lite', stop: null, time: 0 };

        instance.stop = gfx.addTask(`fizzle#${id}`, (now, elapsed) => {
            if (!canvas.isConnected || (o.el && !o.el.isConnected)) {
                stop(id);
                return;
            }
            const dt = Math.min(elapsed, 50) / 1000;
            const t = (instance.time += dt);
            const dx = scroll0.x - scrollX, dy = scroll0.y - scrollY;
            canvas.style.transform = dx || dy ? `translate(${dx}px, ${dy}px)` : '';

            ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
            ctx.clearRect(0, 0, W, H);

            // The "pfft": a faint ring that barely gets going.
            if (t < 0.35) {
                const k = t / 0.35;
                ctx.globalAlpha = 0.35 * (1 - k) * (1 - k);
                ctx.strokeStyle = shades[0];
                ctx.lineWidth = 2 * (1 - k) + 0.5;
                ctx.beginPath();
                ctx.arc(OX, OY, 6 + 18 * Math.sqrt(k), 0, Math.PI * 2);
                ctx.stroke();
            }

            for (const m of motes) {
                if (t >= m.life) continue;
                m.vy += 640 * dt;
                m.vx *= Math.exp(-1.2 * dt);
                m.x += m.vx * dt;
                m.y += m.vy * dt;
                if (m.y > FLOOR) {
                    m.y = FLOOR;
                    if (!m.bounced) {
                        m.vy = -m.vy * 0.34;
                        m.vx *= 0.7;
                        m.bounced = true;
                    } else {
                        m.vy = 0;
                        m.vx *= Math.exp(-6 * dt);
                    }
                }
                const k = t / m.life;
                ctx.globalAlpha = 0.85 * Math.min(1, t / 0.05) * (k < 0.55 ? 1 : 1 - (k - 0.55) / 0.45);
                ctx.fillStyle = m.color;
                ctx.beginPath();
                // Motes resting on the floor squash: they landed, they did not bounce back to life.
                const squash = m.y >= FLOOR - 0.5 ? 0.6 : 1;
                ctx.ellipse(m.x, m.y - m.size * squash, m.size / squash, m.size * squash, 0, 0, Math.PI * 2);
                ctx.fill();
            }
            ctx.globalAlpha = 1;
            if (t >= end) stop(id);
        });
        instances.set(id, instance);
        return id;
    } catch (err) {
        console.warn('[particles] fizzle failed', err?.message ?? err);
        canvas?.remove();
        return 0;
    }
}

/** Immediate teardown of a burst or fizzle. Idempotent; unknown handles are ignored. */
export function stop(handle) {
    const instance = instances.get(handle);
    if (!instance) return;
    instances.delete(handle);
    try { instance.stop?.(); } catch { /* scheduler already dropped it */ }
    try { instance.renderer?.destroy(); } catch { /* already gone */ }
}

/**
 * Whether bursts will take the WebGPU path. Also compiles the pipelines, so calling it when the
 * comic page mounts takes the compile off the frame the score lands on. Never rejects.
 */
export async function describe() {
    try {
        if (!navigator.gpu || forceWebGl({})) return { webgpu: false };
        const device = await getGpuDevice();
        if (!device) return { webgpu: false };
        return { webgpu: !!(await gpuKit(device)) };
    } catch {
        return { webgpu: false };
    }
}

gfx.onTierChanged((tier) => {
    for (const [id, instance] of [...instances]) {
        if (tier === 'off' || (instance.needs === 'full' && tier !== 'full')) stop(id);
    }
});
