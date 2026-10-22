// "3D mode": the comic strip as a tilting diorama.
//
// THE MOMENT THIS IS FOR. A comic is a flat picture of a scene somebody imagined in depth. Tapping
// "3D" turns each panel into a little box you look INTO: the panel border is the front of the box
// and never moves, the backdrop recedes behind it, the characters stand nearer the glass, and as
// you move the pointer (or tilt the phone) the scene shifts against the frame the way a real
// shoebox diorama does. It is opt-in, so unlike the backdrop it is allowed a persistent loop at
// the `lite` tier — but only while the strip is on screen.
//
// WHERE THE DEPTH COMES FROM, cheapest first:
//   1. A HEURISTIC depth map, always, in well under 100ms on the CPU at ~320px. Comic art is
//      drawn in flat regions enclosed by black outlines, so it can be segmented by flooding
//      colour regions between the ink. Regions touching the image edge in paper/ink colours are
//      the gutter; big regions are backdrop (sky recedes, the floor rises toward you); small ones
//      are objects, placed by where they meet the ground and inflated into pillows by their
//      distance from their own outline. Caption boxes go flat ON the glass so text never warps.
//      This is what renders first, so the button responds instantly.
//   2. Depth-Anything-V2-small through transformers.js on WebGPU, lazily imported only when
//      `start` runs on a device with `navigator.gpu`. ~19MB (q4f16) on first use, then the
//      browser cache. When it lands within 30s, its depth morphs over the heuristic one; the
//      gutter, the panel borders and the captions are still pinned to the glass from step 1,
//      because a model trained on photos has no opinion about page furniture.
//   Depth is cached per `src`, so the second toggle is instant and does not re-run anything.
//
// HOW IT RENDERS. One fullscreen triangle per frame: parallax-occlusion mapping (a 8–16 step
// linear march into the height field plus an interpolated refinement), then lighting from the
// height field's own normals — a light that follows the viewer, a rim highlight on silhouettes,
// soft cast shadows from near things onto far ones, crease occlusion, a touch of depth of field
// via explicit mip LOD, and a slight vignette. The canvas sits exactly over the <img>, which stays
// in the DOM (alt text, long-press save) and visible underneath for every failure path.
//
// EDGE ALIGNMENT. Gutter, borders and captions sit on the zero-parallax plane, and displacement
// fades to zero over the outer 2.5% of the image, so the canvas edge is pixel-identical to the
// <img> edge at any tilt — nothing smears in from outside the picture.

import { gfx, createSurface, compileProgram, FULLSCREEN_VERTEX_SHADER, loadImage, createTexture } from './gfx-core.js';

// transformers.js 4.3.1 (2026-10-06). The bundled build: the `.web` build imports
// onnxruntime by bare specifier, which a browser cannot resolve without an import map.
const TRANSFORMERS_URL = 'https://cdn.jsdelivr.net/npm/@huggingface/transformers@4.3.1/dist/transformers.min.js';
const MODEL_ID = 'onnx-community/depth-anything-v2-small';
// q4f16 is 19.1MB and needs `shader-f16`; q4 (27.4MB) is the fallback for adapters without it.
// fp16 (49.6MB) looked no better on comic art and is 2.6x the download.
const MODEL_DTYPE_F16 = 'q4f16';
const MODEL_DTYPE_F32 = 'q4';
const MODEL_WAIT_MS = 30000;
const MODEL_IDLE_DISPOSE_MS = 120000;
const MODEL_INPUT_SIZE = 512;

const HEURISTIC_SIZE = 256;
const PARALLAX = 0.05;          // uv displacement of the deepest point at full tilt, of the short side
const INTRO_MS = 900;
const OUTRO_MS = 320;
const MORPH_MS = 1100;
const IDLE_AFTER_MS = 2200;

const depthCache = new Map();      // src -> { width, height, data: Uint8Array, source }
const analysisCache = new Map();   // src -> heuristic analysis (masks the model path reuses)
const modelInflight = new Map();   // src -> Promise<depth | null>
const instances = new Map();
let nextId = 1;

// ── Model loading (shared across instances) ───────────────────────────────────────────────

const model = {
    dtype: null,
    failed: false,
    pipeline: null,          // Promise<pipeline | null>
    listeners: new Set(),    // (fraction, label) during download
    idleTimer: 0
};

async function pickDtype() {
    if (model.dtype) return model.dtype;
    try {
        const adapter = await navigator.gpu?.requestAdapter();
        if (!adapter) return null;
        model.dtype = adapter.features.has('shader-f16') ? MODEL_DTYPE_F16 : MODEL_DTYPE_F32;
        return model.dtype;
    } catch {
        return null;
    }
}

function broadcast(fraction, label) {
    for (const listener of model.listeners) {
        try { listener(fraction, label); } catch { /* a progress UI failing must not stop the load */ }
    }
}

function loadPipeline() {
    model.pipeline ??= (async () => {
        try {
            const dtype = await pickDtype();
            if (!dtype) throw new Error('no WebGPU adapter');
            const tf = await import(/* webpackIgnore: true */ TRANSFORMERS_URL);
            // Never look for /models/ on this origin: the SPA fallback answers it with index.html.
            tf.env.allowLocalModels = false;
            tf.env.useBrowserCache = true;
            const files = new Map();
            const pipe = await tf.pipeline('depth-estimation', MODEL_ID, {
                device: 'webgpu',
                dtype,
                progress_callback: (info) => {
                    // Per-file sums rather than `progress_total`, so the bar never runs backwards
                    // when a second file starts after the first finished.
                    if (info?.status !== 'progress' || !info.total) return;
                    files.set(info.file, [info.loaded, info.total]);
                    let loaded = 0, total = 0;
                    for (const [l, t] of files.values()) { loaded += l; total += t; }
                    broadcast(0.9 * Math.min(1, loaded / total), 'downloading');
                }
            });
            return { pipe, RawImage: tf.RawImage };
        } catch (err) {
            console.warn('[depth-diorama] model unavailable; using heuristic depth', err);
            model.failed = true;
            model.pipeline = null;
            return null;
        }
    })();
    return model.pipeline;
}

/** Frees the GPU session after a quiet spell; the weights stay in the browser cache. */
function scheduleDispose() {
    clearTimeout(model.idleTimer);
    model.idleTimer = setTimeout(async () => {
        if (modelInflight.size > 0 || !model.pipeline) return;
        const loaded = await model.pipeline;
        model.pipeline = null;
        try { await loaded?.pipe?.dispose?.(); } catch { /* already gone */ }
    }, MODEL_IDLE_DISPOSE_MS);
}

function modelDepth(src, image, analysis) {
    if (!modelInflight.has(src)) {
        const job = (async () => {
            const loaded = await loadPipeline();
            if (!loaded) return null;
            broadcast(0.92, 'estimating');
            const scale = MODEL_INPUT_SIZE / Math.max(image.naturalWidth, image.naturalHeight);
            const canvas = document.createElement('canvas');
            canvas.width = Math.max(14, Math.round(image.naturalWidth * scale));
            canvas.height = Math.max(14, Math.round(image.naturalHeight * scale));
            canvas.getContext('2d').drawImage(image, 0, 0, canvas.width, canvas.height);
            const result = await loaded.pipe(loaded.RawImage.fromCanvas(canvas));
            const raw = result?.depth;
            if (!raw?.data || !raw.width) return null;
            const depth = combineModelDepth(raw, analysis);
            depthCache.set(src, depth);
            return depth;
        })().catch((err) => {
            console.warn('[depth-diorama] depth estimation failed', err);
            return null;
        }).finally(() => {
            modelInflight.delete(src);
            scheduleDispose();
        });
        modelInflight.set(src, job);
    }
    return modelInflight.get(src);
}

// ── Heuristic depth ───────────────────────────────────────────────────────────────────────

/** Two-pass chamfer distance (1, √2) to the nearest pixel where `source` is set. */
function distanceField(source, W, H) {
    const D = Math.SQRT2;
    const d = new Float32Array(W * H);
    for (let i = 0; i < d.length; i++) d[i] = source[i] ? 0 : 1e6;
    for (let y = 0; y < H; y++) {
        for (let x = 0; x < W; x++) {
            const i = y * W + x;
            let v = d[i];
            if (x > 0) v = Math.min(v, d[i - 1] + 1);
            if (y > 0) {
                v = Math.min(v, d[i - W] + 1);
                if (x > 0) v = Math.min(v, d[i - W - 1] + D);
                if (x < W - 1) v = Math.min(v, d[i - W + 1] + D);
            }
            d[i] = v;
        }
    }
    for (let y = H - 1; y >= 0; y--) {
        for (let x = W - 1; x >= 0; x--) {
            const i = y * W + x;
            let v = d[i];
            if (x < W - 1) v = Math.min(v, d[i + 1] + 1);
            if (y < H - 1) {
                v = Math.min(v, d[i + W] + 1);
                if (x < W - 1) v = Math.min(v, d[i + W + 1] + D);
                if (x > 0) v = Math.min(v, d[i + W - 1] + D);
            }
            d[i] = v;
        }
    }
    return d;
}

function find(parent, i) {
    while (parent[i] !== i) {
        parent[i] = parent[parent[i]];
        i = parent[i];
    }
    return i;
}

function union(parent, a, b) {
    a = find(parent, a);
    b = find(parent, b);
    if (a !== b) {
        if (a < b) parent[b] = a; else parent[a] = b;
    }
}

/** Separable box blur, clamped at the edges. */
function boxBlur(src, W, H, r) {
    const tmp = new Float32Array(src.length);
    const out = new Float32Array(src.length);
    const n = 2 * r + 1;
    for (let y = 0; y < H; y++) {
        const row = y * W;
        for (let x = 0; x < W; x++) {
            let s = 0;
            for (let k = -r; k <= r; k++) s += src[row + Math.min(W - 1, Math.max(0, x + k))];
            tmp[row + x] = s / n;
        }
    }
    for (let y = 0; y < H; y++) {
        for (let x = 0; x < W; x++) {
            let s = 0;
            for (let k = -r; k <= r; k++) s += tmp[Math.min(H - 1, Math.max(0, y + k)) * W + x];
            out[y * W + x] = s / n;
        }
    }
    return out;
}

/**
 * Yields to the event loop between phases. Warm, the whole analysis is ~45ms; the first run on
 * a page pays JIT warm-up (~4x), and in one piece that would be a single long task right after
 * the tap. In slices, frames keep flowing while it runs.
 */
const pause = () => globalThis.scheduler?.yield?.() ?? new Promise((resolve) => setTimeout(resolve, 0));

const INK_LUMA = 0.22;
const INK_HALF_WIDTH = 2.6;      // px at HEURISTIC_SIZE: thicker dark areas are fills, not lines
const COLOR_TOLERANCE = 0.16;    // summed |Δrgb| (0..3) between neighbours in one region

/**
 * Segments the comic and assigns a height (1 = the glass, 0 = the back of the box) to every
 * pixel. Returns the depth plus the masks the model path needs to keep page furniture flat.
 */
async function analyse(image) {
    const scale = HEURISTIC_SIZE / Math.max(image.naturalWidth, image.naturalHeight);
    const W = Math.max(16, Math.round(image.naturalWidth * scale));
    const H = Math.max(16, Math.round(image.naturalHeight * scale));
    const N = W * H;
    const canvas = document.createElement('canvas');
    canvas.width = W;
    canvas.height = H;
    const ctx = canvas.getContext('2d', { willReadFrequently: true });
    ctx.drawImage(image, 0, 0, W, H);
    const px = ctx.getImageData(0, 0, W, H).data;

    const R = new Float32Array(N), G = new Float32Array(N), B = new Float32Array(N);
    const lum = new Float32Array(N), sat = new Float32Array(N);
    const light = new Uint8Array(N);
    for (let i = 0; i < N; i++) {
        const r = px[4 * i] / 255, g = px[4 * i + 1] / 255, b = px[4 * i + 2] / 255;
        R[i] = r; G[i] = g; B[i] = b;
        lum[i] = 0.2126 * r + 0.7152 * g + 0.0722 * b;
        const mx = Math.max(r, g, b), mn = Math.min(r, g, b);
        sat[i] = mx > 0 ? (mx - mn) / mx : 0;
        light[i] = lum[i] >= INK_LUMA ? 1 : 0;
    }

    // Ink = thin dark strokes. A dark area thicker than a line is a fill (hair, a night sky).
    await pause();
    const fromLight = distanceField(light, W, H);
    const ink = new Uint8Array(N);
    for (let i = 0; i < N; i++) ink[i] = !light[i] && fromLight[i] <= INK_HALF_WIDTH ? 1 : 0;

    // Regions: flood between the ink lines, joining neighbours of similar colour so a gradient
    // sky stays one region but two flat colours meeting without an outline do not merge.
    const parent = new Int32Array(N);
    for (let i = 0; i < N; i++) parent[i] = i;
    for (let y = 0; y < H; y++) {
        for (let x = 0; x < W; x++) {
            const i = y * W + x;
            if (ink[i]) continue;
            const r = R[i], g = G[i], b = B[i];
            let j = i + 1;
            if (x + 1 < W && !ink[j] && Math.abs(r - R[j]) + Math.abs(g - G[j]) + Math.abs(b - B[j]) < COLOR_TOLERANCE) union(parent, i, j);
            j = i + W;
            if (y + 1 < H && !ink[j] && Math.abs(r - R[j]) + Math.abs(g - G[j]) + Math.abs(b - B[j]) < COLOR_TOLERANCE) union(parent, i, j);
        }
    }

    await pause();
    const label = new Int32Array(N).fill(-1);
    const rootIndex = new Int32Array(N).fill(-1);
    const reg = { area: [], minX: [], minY: [], maxX: [], maxY: [], lum: [], sat: [], first: [] };
    for (let i = 0; i < N; i++) {
        if (ink[i]) continue;
        const root = find(parent, i);
        let k = rootIndex[root];
        if (k < 0) {
            k = rootIndex[root] = reg.area.length;
            reg.area.push(0); reg.lum.push(0); reg.sat.push(0); reg.first.push(i);
            reg.minX.push(W); reg.minY.push(H); reg.maxX.push(0); reg.maxY.push(0);
        }
        const x = i % W, y = (i / W) | 0;
        label[i] = k;
        reg.area[k]++;
        reg.lum[k] += lum[i];
        reg.sat[k] += sat[i];
        if (x < reg.minX[k]) reg.minX[k] = x;
        if (x > reg.maxX[k]) reg.maxX[k] = x;
        if (y < reg.minY[k]) reg.minY[k] = y;
        if (y > reg.maxY[k]) reg.maxY[k] = y;
    }
    const regions = reg.area.length;
    const minArea = Math.max(10, N * 0.0004);
    for (let k = 0; k < regions; k++) {
        reg.lum[k] /= reg.area[k];
        reg.sat[k] /= reg.area[k];
    }
    // Letter counters, specks and anti-aliasing crumbs behave like ink: they take the depth of
    // whatever they sit on.
    for (let i = 0; i < N; i++) {
        if (label[i] >= 0 && reg.area[label[i]] < minArea) {
            label[i] = -1;
            ink[i] = 1;
        }
    }

    await pause();
    // The gutter: paper- or ink-coloured regions that reach the edge of the image.
    const gutter = new Uint8Array(N);
    let anyGutter = false;
    const isGutter = new Uint8Array(regions);
    for (let k = 0; k < regions; k++) {
        if (reg.area[k] < minArea) continue;
        const edge = reg.minX[k] === 0 || reg.minY[k] === 0 || reg.maxX[k] === W - 1 || reg.maxY[k] === H - 1;
        const paper = reg.sat[k] < 0.22 && (reg.lum[k] > 0.8 || reg.lum[k] < 0.16);
        if (edge && paper && reg.area[k] < N * 0.45) {
            isGutter[k] = 1;
            anyGutter = true;
        }
    }
    for (let i = 0; i < N; i++) if (label[i] >= 0 && isGutter[label[i]]) gutter[i] = 1;

    // The frame = gutter + the ink of the panel borders drawn against it.
    const frame = new Uint8Array(N);
    if (anyGutter) {
        const fromGutter = distanceField(gutter, W, H);
        const borderReach = Math.max(3, W * 0.02);
        for (let i = 0; i < N; i++) frame[i] = gutter[i] || (ink[i] && fromGutter[i] <= borderReach) ? 1 : 0;
    }

    await pause();
    // Panels: what the frame encloses. With no gutter the whole picture is one panel.
    for (let i = 0; i < N; i++) parent[i] = i;
    for (let y = 0; y < H; y++) {
        for (let x = 0; x < W; x++) {
            const i = y * W + x;
            if (frame[i]) continue;
            if (x + 1 < W && !frame[i + 1]) union(parent, i, i + 1);
            if (y + 1 < H && !frame[i + W]) union(parent, i, i + W);
        }
    }
    const panelOf = new Int32Array(N).fill(-1);
    rootIndex.fill(-1);
    const panels = [];
    for (let i = 0; i < N; i++) {
        if (frame[i]) continue;
        const root = find(parent, i);
        let p = rootIndex[root];
        if (p < 0) {
            p = rootIndex[root] = panels.length;
            panels.push({ area: 0, minX: W, minY: H, maxX: 0, maxY: 0 });
        }
        const panel = panels[p];
        const x = i % W, y = (i / W) | 0;
        panelOf[i] = p;
        panel.area++;
        if (x < panel.minX) panel.minX = x;
        if (x > panel.maxX) panel.maxX = x;
        if (y < panel.minY) panel.minY = y;
        if (y > panel.maxY) panel.maxY = y;
    }
    for (let i = 0; i < N; i++) {
        const p = panelOf[i];
        if (p >= 0 && panels[p].area < N * 0.025) {
            frame[i] = 1;
            panelOf[i] = -1;
        }
    }

    await pause();
    // Distance of every pixel from its own region's outline, for the pillow inflation.
    const boundary = new Uint8Array(N);
    for (let y = 0; y < H; y++) {
        for (let x = 0; x < W; x++) {
            const i = y * W + x;
            const k = label[i];
            boundary[i] = k < 0
                || x === 0 || label[i - 1] !== k
                || x === W - 1 || label[i + 1] !== k
                || y === 0 || label[i - W] !== k
                || y === H - 1 || label[i + W] !== k ? 1 : 0;
        }
    }
    const inner = distanceField(boundary, W, H);
    const maxInner = new Float32Array(regions);
    for (let i = 0; i < N; i++) if (label[i] >= 0 && inner[i] > maxInner[label[i]]) maxInner[label[i]] = inner[i];

    await pause();
    // Classify each region within its panel.
    const kind = new Uint8Array(regions);          // 0 object, 1 sky, 2 floor, 3 glass
    for (let k = 0; k < regions; k++) {
        if (reg.area[k] < minArea || isGutter[k]) continue;
        const p = panelOf[reg.first[k]];
        if (p < 0) continue;
        const panel = panels[p];
        const pw = panel.maxX - panel.minX + 1, ph = panel.maxY - panel.minY + 1;
        const bw = reg.maxX[k] - reg.minX[k] + 1, bh = reg.maxY[k] - reg.minY[k] + 1;
        const topRel = (reg.minY[k] - panel.minY) / ph;
        const fill = reg.area[k] / (bw * bh);
        if (reg.lum[k] > 0.8 && reg.sat[k] < 0.45 && bw > bh * 1.6 && topRel < 0.38 && fill > 0.4
            && reg.area[k] < panel.area * 0.25) {
            kind[k] = 3;   // a caption box: on the glass, so the lettering never moves
        } else if (reg.area[k] >= panel.area * 0.16 || bw >= pw * 0.9 || bh >= ph * 0.9) {
            kind[k] = topRel < 0.2 ? 1 : 2;
        }
    }

    const height = new Float32Array(N).fill(-1);
    for (let i = 0; i < N; i++) {
        const k = label[i];
        if (k < 0 || frame[i]) continue;
        const p = panelOf[i];
        if (p < 0) continue;
        const panel = panels[p];
        const ph = panel.maxY - panel.minY + 1;
        const y = (i / W) | 0;
        const t = (y - panel.minY) / ph;
        switch (kind[k]) {
            case 3:
                height[i] = 1;
                break;
            case 1:
                height[i] = 0.04 + 0.1 * t;
                break;
            case 2: {
                const g = Math.max(0, (y - reg.minY[k]) / Math.max(1, panel.maxY - reg.minY[k]));
                height[i] = 0.12 + 0.4 * Math.pow(g, 1.25);
                break;
            }
            default: {
                // Placed by where it meets the floor (lower = nearer), then inflated.
                const bottom = (reg.maxY[k] - panel.minY) / ph;
                const size = Math.min(1, Math.max(0.35, Math.sqrt(reg.area[k] / panel.area) * 4));
                const swell = Math.sqrt(Math.min(1, inner[i] / Math.max(1, maxInner[k])));
                height[i] = 0.46 + 0.26 * bottom + 0.2 * size * swell;
            }
        }
    }

    await pause();
    // Outlines belong to the nearer of the shapes they separate: a max filter over assigned
    // neighbours, applied to ink only, so a line never drags a whole ink network forward.
    for (let pass = 0; pass < 3; pass++) {
        const src = height.slice();
        for (let y = 0; y < H; y++) {
            for (let x = 0; x < W; x++) {
                const i = y * W + x;
                if (src[i] >= 0 || frame[i]) continue;
                let best = -1;
                for (let dy = -2; dy <= 2; dy++) {
                    const yy = y + dy;
                    if (yy < 0 || yy >= H) continue;
                    for (let dx = -2; dx <= 2; dx++) {
                        const xx = x + dx;
                        if (xx < 0 || xx >= W) continue;
                        const v = src[yy * W + xx];
                        if (v > best) best = v;
                    }
                }
                if (best >= 0) height[i] = best;
            }
        }
    }

    await pause();
    const pin = new Uint8Array(N);
    for (let i = 0; i < N; i++) {
        if (frame[i]) height[i] = 1;
        else if (height[i] < 0) height[i] = 0.3;
        pin[i] = height[i] >= 0.999 ? 1 : 0;
    }

    const depth = finish(height, pin, W, H);
    return { width: W, height: H, data: depth, pin, panelOf, panels: panels.length, source: 'heuristic' };
}

/** Light anti-aliasing blur, then the glass is re-pinned so borders and captions stay crisp. */
function finish(height, pin, W, H) {
    const soft = boxBlur(height, W, H, 1);
    const out = new Uint8Array(W * H);
    for (let i = 0; i < out.length; i++) out[i] = pin[i] ? 255 : Math.round(Math.min(1, Math.max(0, soft[i])) * 255);
    return out;
}

/**
 * Model depth, normalised per panel (2nd–98th percentile, so one deep panel does not flatten
 * the other) and mapped to the same range as the heuristic, with the heuristic's glass pinned.
 */
function combineModelDepth(raw, analysis) {
    const MW = raw.width, MH = raw.height, channels = raw.channels ?? 1;
    const { width: W, height: H, pin, panelOf, panels, data: heur } = analysis;
    const map = new Int32Array(MW * MH);
    for (let y = 0; y < MH; y++) {
        const hy = Math.min(H - 1, Math.floor((y + 0.5) * H / MH));
        for (let x = 0; x < MW; x++) map[y * MW + x] = hy * W + Math.min(W - 1, Math.floor((x + 0.5) * W / MW));
    }

    const hist = Array.from({ length: Math.max(1, panels) }, () => new Uint32Array(256));
    for (let i = 0; i < map.length; i++) {
        const p = panelOf[map[i]];
        if (p >= 0 && !pin[map[i]]) hist[p][raw.data[i * channels]]++;
    }
    const range = hist.map((h) => {
        let total = 0;
        for (const c of h) total += c;
        const pick = (q) => {
            let acc = 0;
            for (let v = 0; v < 256; v++) { acc += h[v]; if (acc >= total * q) return v; }
            return 255;
        };
        const lo = pick(0.02), hi = pick(0.98);
        return [lo, Math.max(lo + 8, hi)];
    });

    const height = new Float32Array(MW * MH);
    const pinned = new Uint8Array(MW * MH);
    for (let i = 0; i < map.length; i++) {
        const j = map[i];
        if (pin[j]) {
            height[i] = 1;
            pinned[i] = 1;
            continue;
        }
        const p = panelOf[j];
        const [lo, hi] = range[Math.max(0, p)];
        const m = Math.min(1, Math.max(0, (raw.data[i * channels] - lo) / (hi - lo)));
        // A little of the heuristic keeps region edges crisp where the model is soft.
        height[i] = 0.85 * (0.04 + 0.82 * m) + 0.15 * (heur[j] / 255);
    }
    return { width: MW, height: MH, data: finish(height, pinned, MW, MH), source: 'model' };
}

// ── Rendering ─────────────────────────────────────────────────────────────────────────────

const FRAGMENT_SHADER = `#version 300 es
precision highp float;
in vec2 vUv;
out vec4 outColor;

uniform sampler2D uImage;     // FLIP_Y applied: origin bottom-left, like vUv
uniform sampler2D uDepthA;    // rows top-down
uniform sampler2D uDepthB;
uniform float uMix;           // A -> B morph
uniform vec2  uTilt;          // -1..1; +x viewer to the right, +y viewer above
uniform vec2  uShift;         // uv shift of the deepest point at |tilt| = 1
uniform vec2  uImageSize;
uniform vec2  uDepthTexel;
uniform float uAmount;        // intro/outro 0..1
uniform float uDebug;
uniform float uDark;
uniform vec3  uRim;
uniform vec3  uShade;

float heightAt(vec2 uv) {
    vec2 d = vec2(uv.x, 1.0 - uv.y);
    return mix(texture(uDepthA, d).r, texture(uDepthB, d).r, uMix);
}

float heightLod(vec2 uv, float lod) {
    vec2 d = vec2(uv.x, 1.0 - uv.y);
    return mix(textureLod(uDepthA, d, lod).r, textureLod(uDepthB, d, lod).r, uMix);
}

void main() {
    if (uDebug > 0.5) {
        float h = heightAt(vUv);
        outColor = vec4(vec3(h), 1.0);
        return;
    }

    // Displacement fades to nothing at the outer edge, so the canvas edge is the <img> edge.
    vec2 edge = min(vUv, 1.0 - vUv) / (uShift * 0.5 + 0.0001);
    float edgeFade = smoothstep(0.0, 1.0, min(edge.x, edge.y));
    vec2 tilt = uTilt * uAmount;
    vec2 S = tilt * uShift * edgeFade;

    // Parallax occlusion: march from the glass (depth 0) into the box until the ray is below
    // the surface, more steps the more oblique the view.
    float steps = mix(8.0, 16.0, clamp(length(tilt), 0.0, 1.0));
    float stepD = 1.0 / steps;
    vec2 stepUv = -S * stepD;
    vec2 uv = vUv;
    float rayD = 0.0;
    float surfD = 1.0 - heightAt(uv);
    for (int i = 0; i < 16; i++) {
        if (float(i) >= steps || rayD >= surfD) break;
        uv += stepUv;
        rayD += stepD;
        surfD = 1.0 - heightAt(uv);
    }
    // Refinement: intersect the segment between the last two samples with the surface.
    vec2 prevUv = uv - stepUv;
    float after = surfD - rayD;
    float before = (1.0 - heightAt(prevUv)) - (rayD - stepD);
    float w = clamp(after / (after - before + 1e-5), 0.0, 1.0);
    uv = mix(uv, prevUv, w);

    float h = heightAt(uv);
    float depth = 1.0 - h;

    // Normal from the height field's own slope, over two texels so 8-bit steps do not band.
    vec2 tx = vec2(uDepthTexel.x * 2.0, 0.0), ty = vec2(0.0, uDepthTexel.y * 2.0);
    float dx = heightAt(uv + tx) - heightAt(uv - tx);
    float dy = heightAt(uv + ty) - heightAt(uv - ty);
    vec3 N = normalize(vec3(-dx * 3.0, -dy * 3.0, 1.0));

    // A height field can only extrude: viewed obliquely, a cut-out's silhouette becomes a wall
    // down to the backdrop, textured with its outline smeared across the jump. A diorama card
    // is thin, so a wall is really the backdrop BEHIND it coming into view. Paint it as that:
    // the colour just past the foot of the slope, and treat the surface as flat there.
    float wall = smoothstep(0.32, 0.6, 1.0 - N.z) * uAmount;
    vec2 downhill = -normalize(vec2(dx, dy) + 1e-6) * uDepthTexel * 3.0;
    vec2 foot = heightAt(uv + downhill * 2.0) < heightAt(uv + downhill) - 0.02 ? uv + downhill * 2.0 : uv + downhill;
    vec2 colorUv = mix(uv, foot, step(0.001, wall));
    N = normalize(mix(N, vec3(0.0, 0.0, 1.0), wall));
    float slope = 1.0 - N.z;

    // A light that follows the viewer, from slightly above-left.
    vec3 L = normalize(vec3(tilt * 0.9 + vec2(-0.25, 0.35), 0.8));
    vec3 V = normalize(vec3(tilt * 0.6, 1.0));
    float shade = clamp(dot(N, L) / L.z, 0.6, 1.25);

    // Explicit LOD: derivatives of the displaced uv spike at occlusion edges and would pop mips.
    vec2 texelsPerPixel = fwidth(vUv) * uImageSize;
    float baseLod = max(0.0, log2(max(texelsPerPixel.x, texelsPerPixel.y)));
    float dof = 1.4 * smoothstep(0.55, 1.0, depth) * clamp(length(tilt) * 1.6 + 0.3, 0.0, 1.0);
    vec3 surfaceColor = textureLod(uImage, uv, baseLod + dof).rgb;
    vec3 behind = textureLod(uImage, colorUv, baseLod + dof + 0.5).rgb;
    vec3 color = mix(surfaceColor, behind, wall);

    // Crease occlusion: darker where the surface sits below its surroundings — which is also the
    // soft contact shadow every card throws on the backdrop just behind it.
    float around = heightLod(uv, 3.0);
    float ao = smoothstep(0.0, 0.28, around - h);
    // Soft shadow cast by whatever stands between this point and the light.
    vec2 toLight = normalize(L.xy + 1e-4) * uShift * 0.55;
    float occluder = heightLod(uv + toLight, 1.5);
    float shadow = smoothstep(0.05, 0.35, occluder - h);
    float darken = clamp(ao * 0.4 + shadow * 0.36 + wall * 0.25, 0.0, 0.72) * uAmount;

    color = mix(color, color * shade, 0.6 * uAmount);
    color = mix(color, color * uShade, darken);

    // Rim light on the rounded parts that face the light, and a faint gloss sweeping with the tilt.
    float facing = max(dot(normalize(N.xy + 1e-4), normalize(L.xy + 1e-4)), 0.0);
    float rim = smoothstep(0.03, 0.2, slope) * facing;
    float gloss = pow(max(dot(N, normalize(L + V)), 0.0), 48.0) * (1.0 - wall);
    color += uRim * (rim * 0.28 + gloss * 0.06) * uAmount;

    // Slight vignette, so the box reads as lit from the front.
    vec2 c = vUv - 0.5;
    color *= 1.0 - uAmount * 0.16 * smoothstep(0.25, 0.75, length(c * vec2(1.0, 0.9)) * 1.25);

    outColor = vec4(clamp(color, 0.0, 1.0), 1.0);
}`;

function readToken(name, fallback) {
    try {
        const value = getComputedStyle(document.documentElement).getPropertyValue(name).trim();
        const m = /^#([0-9a-f]{6})$/i.exec(value);
        if (!m) return fallback;
        const n = parseInt(m[1], 16);
        return [(n >> 16 & 255) / 255, (n >> 8 & 255) / 255, (n & 255) / 255];
    } catch {
        return fallback;
    }
}

function isDark() {
    const theme = document.documentElement.dataset.theme;
    if (theme) return theme === 'dark';
    try { return matchMedia('(prefers-color-scheme: dark)').matches; } catch { return false; }
}

/** Light: a warm rim of accent-tinted white over comic-ink shade. Dark: a cooler brand rim. */
function readPalette() {
    const dark = isDark();
    const accent = readToken(dark ? '--color-brand' : '--color-accent', [0.96, 0.62, 0.04]);
    const rim = accent.map((c) => 0.7 + 0.3 * c);
    const shade = readToken(dark ? '--color-surface' : '--color-comic-ink', [0.12, 0.12, 0.18]).map((c) => Math.min(1, c * 1.3 + 0.08));
    return { dark, rim, shade };
}

function uploadDepth(gl, texture, depth) {
    gl.bindTexture(gl.TEXTURE_2D, texture);
    gl.pixelStorei(gl.UNPACK_ALIGNMENT, 1);
    gl.pixelStorei(gl.UNPACK_FLIP_Y_WEBGL, false);
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.R8, depth.width, depth.height, 0, gl.RED, gl.UNSIGNED_BYTE, depth.data);
    gl.generateMipmap(gl.TEXTURE_2D);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR_MIPMAP_LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
    gl.pixelStorei(gl.UNPACK_ALIGNMENT, 4);
    gl.bindTexture(gl.TEXTURE_2D, null);
}

/** Puts the canvas exactly over the img's layout box (used values, so fractions survive). */
function align(canvas, img, container) {
    try {
        const cs = getComputedStyle(img);
        const ccs = getComputedStyle(container);
        canvas.style.left = `${parseFloat(cs.marginLeft) + parseFloat(ccs.paddingLeft) + parseFloat(cs.borderLeftWidth)}px`;
        canvas.style.top = `${img.offsetTop + parseFloat(cs.borderTopWidth)}px`;
        canvas.style.width = `${img.clientWidth ? parseFloat(cs.width) - parseFloat(cs.borderLeftWidth) - parseFloat(cs.borderRightWidth) : 0}px`;
        canvas.style.height = `${img.clientHeight ? parseFloat(cs.height) - parseFloat(cs.borderTopWidth) - parseFloat(cs.borderBottomWidth) : 0}px`;
    } catch {
        // Detached; the frame task tears down.
    }
}

const smooth = (t) => t * t * (3 - 2 * t);

// ── Public API ────────────────────────────────────────────────────────────────────────────

/** WebGL2 present and the effects tier at least `lite`. */
export function supported() {
    try {
        return gfx.describe().webgl2 && gfx.allows('lite');
    } catch {
        return false;
    }
}

/** What the model path would use on this device. */
export function describe() {
    const webgpu = typeof navigator !== 'undefined' && !!navigator.gpu;
    return {
        webgpu,
        model: webgpu && !model.failed ? `${MODEL_ID} (${model.dtype ?? MODEL_DTYPE_F16})` : 'heuristic'
    };
}

/**
 * @param {HTMLElement} container the .comic-strip-container holding the .comic-strip-image
 * @param {{ src: string, onProgress?: (fraction: number, label: string) => void,
 *           onEnd?: (reason: string) => void, preferModel?: boolean, debug?: 'depth' }} options
 * @returns {Promise<number>} handle, or 0 when nothing started (the plain img stays as it is)
 */
export async function start(container, options = {}) {
    try {
        // iOS asks for motion permission, and only inside a user gesture: so first, before any await.
        let permission = null;
        try {
            if (typeof DeviceOrientationEvent !== 'undefined' && typeof DeviceOrientationEvent.requestPermission === 'function') {
                permission = DeviceOrientationEvent.requestPermission().catch(() => 'denied');
            }
        } catch { /* not in a gesture; pointer and idle orbit still work */ }

        if (!container || !options.src || !supported()) return 0;
        const img = container.querySelector('img');
        if (!img) return 0;

        for (const [id, existing] of instances) {
            if (existing.container === container) end(id, 'replaced', true);
        }

        const onProgress = typeof options.onProgress === 'function' ? options.onProgress : null;
        const report = (fraction, label) => {
            try { onProgress?.(fraction, label); } catch { /* UI only */ }
        };

        const src = options.src;
        const image = await loadImage(src, 10000);
        if (!image || !container.isConnected || !supported()) return 0;

        if (!analysisCache.has(src)) {
            const began = performance.now();
            analysisCache.set(src, analyse(image).catch(() => null));
            if (options.debug) {
                analysisCache.get(src).then((a) => console.debug(`[depth-diorama] heuristic depth ${a?.width}x${a?.height} in ${(performance.now() - began).toFixed(1)}ms`));
            }
        }
        const analysis = await analysisCache.get(src);
        if (!analysis) {
            analysisCache.delete(src);
            return 0;
        }
        if (!container.isConnected || !supported()) return 0;
        const cached = depthCache.get(src);
        const initial = cached ?? analysis;

        const canvas = document.createElement('canvas');
        canvas.className = 'depth-diorama-canvas';
        canvas.setAttribute('aria-hidden', 'true');
        Object.assign(canvas.style, { position: 'absolute', display: 'block', pointerEvents: 'none' });
        align(canvas, img, container);
        container.appendChild(canvas);

        const surface = createSurface(canvas, { maxDpr: 1.5 });
        if (!surface) {
            canvas.remove();
            return 0;
        }
        const gl = surface.gl;
        let program;
        try {
            program = compileProgram(gl, FULLSCREEN_VERTEX_SHADER, FRAGMENT_SHADER);
        } catch (err) {
            console.warn('[depth-diorama]', err);
            surface.release();
            canvas.remove();
            return 0;
        }
        const u = (name) => gl.getUniformLocation(program, name);
        const loc = {
            image: u('uImage'), depthA: u('uDepthA'), depthB: u('uDepthB'), mix: u('uMix'), tilt: u('uTilt'),
            shift: u('uShift'), imageSize: u('uImageSize'), depthTexel: u('uDepthTexel'), amount: u('uAmount'),
            debug: u('uDebug'), dark: u('uDark'), rim: u('uRim'), shade: u('uShade')
        };
        const vao = gl.createVertexArray();
        const imageTex = createTexture(gl, image, { mipmaps: true });
        const depthA = gl.createTexture();
        const depthB = gl.createTexture();
        uploadDepth(gl, depthA, initial);
        uploadDepth(gl, depthB, initial);

        const id = nextId++;
        const now = performance.now();
        const inst = {
            container, canvas, img, surface, gl, program, loc, vao, imageTex, depthA, depthB,
            depthSize: [initial.width, initial.height],
            morphFrom: 0,                 // performance.now() of a running A->B morph, or 0
            startedAt: now,
            endingAt: 0,
            visible: true,
            stopTask: null,
            listeners: [],
            observers: [],
            palette: readPalette(),
            tilt: [0, 0], tiltMid: [0, 0], input: [0, 0], inputWeight: 0, lastInputAt: -1e9,
            orient: null,
            debug: options.debug === 'depth',
            onEnd: typeof options.onEnd === 'function' ? options.onEnd : null,
            progress: null
        };
        instances.set(id, inst);
        container.dataset.depthDiorama = 'running';

        const listen = (target, type, fn, opts) => {
            target.addEventListener(type, fn, opts);
            inst.listeners.push(() => target.removeEventListener(type, fn, opts));
        };

        listen(canvas, 'webglcontextlost', (e) => { e.preventDefault(); end(id, 'context-lost', true); });

        listen(container, 'pointermove', (e) => {
            const r = container.getBoundingClientRect();
            if (!r.width || !r.height) return;
            inst.input[0] = Math.max(-1, Math.min(1, ((e.clientX - r.left) / r.width) * 2 - 1));
            inst.input[1] = Math.max(-1, Math.min(1, 1 - ((e.clientY - r.top) / r.height) * 2));
            inst.lastInputAt = performance.now();
        }, { passive: true });

        listen(window, 'deviceorientation', (e) => {
            if (e.beta == null || e.gamma == null) return;
            const t = performance.now();
            // A slowly drifting baseline: whatever angle the phone is held at becomes "flat".
            if (!inst.orient) inst.orient = { beta: e.beta, gamma: e.gamma, at: t };
            const o = inst.orient;
            const k = Math.min(1, (t - o.at) / 4000);
            o.beta += (e.beta - o.beta) * k;
            o.gamma += (e.gamma - o.gamma) * k;
            o.at = t;
            let x = -(e.gamma - o.gamma) / 18, y = -(e.beta - o.beta) / 18;
            const angle = ((screen.orientation?.angle ?? 0) * Math.PI) / 180;
            if (angle) [x, y] = [x * Math.cos(angle) - y * Math.sin(angle), x * Math.sin(angle) + y * Math.cos(angle)];
            if (Math.abs(x) + Math.abs(y) < 0.02) return;
            inst.input[0] = Math.max(-1, Math.min(1, x));
            inst.input[1] = Math.max(-1, Math.min(1, y));
            inst.lastInputAt = t;
        }, { passive: true });

        try {
            const resize = new ResizeObserver(() => align(canvas, img, container));
            resize.observe(img);
            resize.observe(container);
            inst.observers.push(resize);
            const seen = new IntersectionObserver((entries) => {
                inst.visible = entries[entries.length - 1].isIntersecting;
                schedule(id);
            });
            seen.observe(container);
            inst.observers.push(seen);
        } catch { /* older engines: always render while running */ }

        inst.unsubscribeTier = gfx.onTierChanged(() => {
            if (!gfx.allows('lite')) end(id, 'tier', true);
        });

        schedule(id);

        // Depth upgrade: the model, if it can run here and this src has not had it yet.
        if (cached) {
            report(1, cached.source);
        } else if (options.preferModel !== false && navigator.gpu && !model.failed) {
            const progress = (f, label) => { if (instances.has(id)) report(f, label); };
            model.listeners.add(progress);
            inst.progress = progress;
            report(0, 'downloading');
            const timeout = new Promise((resolve) => setTimeout(() => resolve('timeout'), MODEL_WAIT_MS));
            Promise.race([modelDepth(src, image, analysis), timeout]).then((result) => {
                model.listeners.delete(progress);
                const live = instances.get(id);
                if (!live || live.endingAt) return;
                if (result && result !== 'timeout') {
                    morphTo(live, result);
                    report(1, 'model');
                } else {
                    report(1, 'heuristic');
                }
            });
        } else {
            report(1, 'heuristic');
        }

        void permission;
        return id;
    } catch (err) {
        console.warn('[depth-diorama] start failed', err);
        return 0;
    }
}

/** Flattens the diorama and removes it. Idempotent; safe with 0 or an unknown handle. */
export function stop(handle) {
    try {
        end(handle, 'stopped', false);
    } catch {
        // Nothing to undo.
    }
}

function morphTo(inst, depth) {
    try {
        const { gl } = inst;
        uploadDepth(gl, inst.depthB, depth);
        inst.nextSize = [depth.width, depth.height];
        inst.morphFrom = performance.now();
    } catch {
        // Keep the depth already showing.
    }
}

/** Runs the frame task only while the strip is visible and the instance is alive. */
function schedule(id) {
    const inst = instances.get(id);
    if (!inst) return;
    const want = inst.visible || inst.endingAt;
    if (want && !inst.stopTask) {
        inst.lastFrame = 0;
        inst.stopTask = gfx.addTask(`depth-diorama#${id}`, (now) => render(id, now));
    } else if (!want && inst.stopTask) {
        inst.stopTask();
        inst.stopTask = null;
    }
}

function render(id, now) {
    const inst = instances.get(id);
    if (!inst) return;
    if (!inst.container.isConnected || !inst.canvas.isConnected) {
        end(id, 'detached', true);
        return;
    }
    const dt = Math.min(0.1, inst.lastFrame ? (now - inst.lastFrame) / 1000 : 0.016);
    inst.lastFrame = now;

    // Input weight rises at once on movement and decays to the idle orbit after a pause.
    const active = now - inst.lastInputAt < IDLE_AFTER_MS;
    inst.inputWeight += ((active ? 1 : 0) - inst.inputWeight) * (1 - Math.exp(-dt / (active ? 0.15 : 1.4)));
    const t = now / 1000;
    const orbit = [0.62 * Math.sin(t * 0.42), 0.38 * Math.sin(t * 0.29 + 1.1)];
    const target = [
        orbit[0] + (inst.input[0] - orbit[0]) * inst.inputWeight,
        orbit[1] + (inst.input[1] - orbit[1]) * inst.inputWeight
    ];
    // Two cascaded low-passes: smooth to the second derivative and incapable of overshoot,
    // which is the difference between a camera that glides and one that wobbles.
    const a = 1 - Math.exp(-dt / 0.09);
    for (let k = 0; k < 2; k++) {
        inst.tiltMid[k] += (target[k] - inst.tiltMid[k]) * a;
        inst.tilt[k] += (inst.tiltMid[k] - inst.tilt[k]) * a;
    }

    let amount = smooth(Math.min(1, (now - inst.startedAt) / INTRO_MS));
    if (inst.endingAt) {
        const out = Math.min(1, (now - inst.endingAt) / OUTRO_MS);
        amount *= 1 - smooth(out);
        if (out >= 1) {
            teardown(id);
            return;
        }
    }

    let mix = 0;
    if (inst.morphFrom) {
        const m = Math.min(1, (now - inst.morphFrom) / MORPH_MS);
        mix = smooth(m);
        if (m >= 1) {
            // Settle: B becomes A, so steady state samples one texture's worth of change.
            [inst.depthA, inst.depthB] = [inst.depthB, inst.depthA];
            inst.depthSize = inst.nextSize;
            inst.morphFrom = 0;
            mix = 0;
        }
    }

    const { surface, gl, loc } = inst;
    if (!surface.beginFrame()) return;
    const aspect = surface.width / Math.max(1, surface.height);
    const shift = aspect >= 1 ? [PARALLAX / aspect, PARALLAX] : [PARALLAX, PARALLAX * aspect];

    gl.useProgram(inst.program);
    gl.bindVertexArray(inst.vao);
    gl.activeTexture(gl.TEXTURE0);
    gl.bindTexture(gl.TEXTURE_2D, inst.imageTex);
    gl.activeTexture(gl.TEXTURE1);
    gl.bindTexture(gl.TEXTURE_2D, inst.depthA);
    gl.activeTexture(gl.TEXTURE2);
    gl.bindTexture(gl.TEXTURE_2D, inst.depthB);
    gl.uniform1i(loc.image, 0);
    gl.uniform1i(loc.depthA, 1);
    gl.uniform1i(loc.depthB, 2);
    gl.uniform1f(loc.mix, mix);
    gl.uniform2f(loc.tilt, inst.tilt[0], inst.tilt[1]);
    gl.uniform2f(loc.shift, shift[0], shift[1]);
    gl.uniform2f(loc.imageSize, inst.img.naturalWidth || surface.width, inst.img.naturalHeight || surface.height);
    gl.uniform2f(loc.depthTexel, 1 / inst.depthSize[0], 1 / inst.depthSize[1]);
    gl.uniform1f(loc.amount, amount);
    gl.uniform1f(loc.debug, inst.debug ? 1 : 0);
    gl.uniform1f(loc.dark, inst.palette.dark ? 1 : 0);
    gl.uniform3fv(loc.rim, inst.palette.rim);
    gl.uniform3fv(loc.shade, inst.palette.shade);
    gl.drawArrays(gl.TRIANGLES, 0, 3);
    surface.present();
}

/** `immediate` skips the flatten-out: the canvas may already be useless (context lost, detached). */
function end(id, reason, immediate) {
    const inst = instances.get(id);
    if (!inst) return;
    for (const off of inst.listeners.splice(0)) {
        try { off(); } catch { /* already gone */ }
    }
    if (inst.progress) model.listeners.delete(inst.progress);
    if (reason !== 'stopped' && inst.onEnd) {
        try { inst.onEnd(reason); } catch { /* UI only */ }
        inst.onEnd = null;
    }
    if (immediate || !inst.container.isConnected || !inst.visible) {
        teardown(id);
        return;
    }
    if (!inst.endingAt) {
        inst.endingAt = performance.now();
        schedule(id);
    }
}

function teardown(id) {
    const inst = instances.get(id);
    if (!inst) return;
    instances.delete(id);
    inst.stopTask?.();
    inst.unsubscribeTier?.();
    for (const o of inst.observers) {
        try { o.disconnect(); } catch { /* fine */ }
    }
    for (const off of inst.listeners) {
        try { off(); } catch { /* fine */ }
    }
    if (inst.progress) model.listeners.delete(inst.progress);
    try { inst.surface.release(); } catch { /* fine */ }
    try { inst.canvas.remove(); } catch { /* fine */ }
    try {
        if (![...instances.values()].some((other) => other.container === inst.container)) {
            delete inst.container.dataset.depthDiorama;
        }
    } catch { /* detached */ }
}
