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
//   1. A HEURISTIC depth map, always, on the CPU at 256px. Comic art is flat regions enclosed by
//      black outlines, so it segments by flooding colour between the ink. Paper/ink regions at
//      the image edge are the gutter; big regions are backdrop (sky recedes, the floor rises
//      toward you); smaller ones are cut-out cards, placed by where they meet the floor and
//      gently inflated. Caption boxes go ON the glass so lettering never moves. ~45ms warm; the
//      first run on a page pays JIT warm-up (~300ms here), so it yields between phases.
//   2. Depth-Anything-V2-small through transformers.js on WebGPU, in a worker, imported only
//      when `start` runs on a device with `navigator.gpu`. ~19MB (q4f16) on first use, then the
//      browser cache. If it lands within 30s it morphs over the heuristic map. It decides each
//      card's place and a little of its relief; the segmentation still decides where a card
//      ends (a network's soft edges under parallax are a rubber sheet) and what is page furniture.
//   Both are cached per `src`, so a second toggle is instant.
//
// HOW IT RENDERS. One fullscreen triangle: parallax-occlusion mapping (8–16 linear steps into the
// height field, then bisection), with the scene treated as THIN CARDS over a backdrop layer: where
// the march grazes the side of a card, it shows the backdrop behind it instead of an extruded
// wall. Lit from the upper left like a drawn comic: soft cast shadows of cards on what is behind
// them, a little relief shading and rim light on the cards, a touch of depth of field by explicit
// mip LOD, and a slight vignette. Resolution adapts to the frame time.
//
// EDGE ALIGNMENT. The canvas sits exactly over the <img>, which stays in the DOM (alt text,
// long-press save) and shows through on every failure path. Gutter, borders and captions sit on
// the zero-parallax plane, and displacement fades to zero at the outer edge, so the canvas edge
// matches the <img> edge at any tilt.

import { gfx, createSurface, compileProgram, FULLSCREEN_VERTEX_SHADER, loadImage, createTexture } from './gfx-core.js';

// transformers.js 4.3.1 (2026-10-06). The bundled build: the `.web` build imports
// onnxruntime by bare specifier, which a browser cannot resolve without an import map.
const TRANSFORMERS_URL = 'https://cdn.jsdelivr.net/npm/@huggingface/transformers@4.3.1/dist/transformers.min.js';
const MODEL_ID = 'onnx-community/depth-anything-v2-small';
// q4f16 is 19.1MB and needs `shader-f16`; q4 (27.4MB) is the fallback for adapters without it.
// fp16 (49.6MB) is 2.6x the download for a map that ends up 8-bit and segmentation-guided anyway.
const MODEL_DTYPE_F16 = 'q4f16';
const MODEL_DTYPE_F32 = 'q4';
const MODEL_WAIT_MS = 30000;
const MODEL_IDLE_DISPOSE_MS = 120000;
const MODEL_INPUT_SIZE = 512;

const HEURISTIC_SIZE = 256;
const MAX_DPR = 1.5;
const PARALLAX = 0.05;          // uv displacement of the deepest point at full tilt, of the short side
const INTRO_MS = 900;
const OUTRO_MS = 320;
const MORPH_MS = 1100;
const IDLE_AFTER_MS = 2200;
const CACHE_LIMIT = 4;

const depthCache = new Map();      // src -> model depth { width, height, data, fg, back, source }
const analysisCache = new Map();   // src -> Promise<{ image, analysis } | null> (the model path reuses its masks)
const modelInflight = new Map();   // src -> Promise<depth | null>
const instances = new Map();
let nextId = 1;

// ── Model (shared across instances, run in a worker) ──────────────────────────────────────
//
// In a worker because creating the ONNX session compiles WebAssembly and WebGPU pipelines for
// seconds of main-thread time: on the page itself that stalled the shared frame loop long
// enough for gfx-core to auto-downgrade the tier to `off`, which ended the effect it was
// loading for. The worker is a Blob module, so there is no extra file to deploy.

const WORKER_SOURCE = `
let loading = null;
const fail = (id, err) => self.postMessage({ type: 'error', id, message: String(err && err.message || err) });
self.onmessage = async (event) => {
    const msg = event.data;
    try {
        loading ??= (async () => {
            const tf = await import(${JSON.stringify(TRANSFORMERS_URL)});
            // Never look for /models/ on the page's origin: the SPA fallback answers with index.html.
            tf.env.allowLocalModels = false;
            tf.env.useBrowserCache = true;
            const pipe = await tf.pipeline('depth-estimation', ${JSON.stringify(MODEL_ID)}, {
                device: 'webgpu',
                dtype: msg.dtype,
                progress_callback: (info) => {
                    if (info && info.status === 'progress' && info.total) {
                        self.postMessage({ type: 'progress', file: info.file, loaded: info.loaded, total: info.total });
                    }
                }
            });
            return { pipe, RawImage: tf.RawImage };
        })();
        const { pipe, RawImage } = await loading;
        self.postMessage({ type: 'estimating', id: msg.id });
        const result = await pipe(new RawImage(msg.data, msg.width, msg.height, 4));
        const depth = result.depth;
        const data = new Uint8Array(depth.data);
        self.postMessage({ type: 'depth', id: msg.id, width: depth.width, height: depth.height, channels: depth.channels, data }, [data.buffer]);
    } catch (err) {
        loading = null;
        fail(msg.id, err);
    }
};`;

const model = {
    dtype: null,
    failed: false,
    worker: null,
    jobs: new Map(),         // job id -> { resolve }
    nextJob: 1,
    files: new Map(),        // download progress per model file
    reported: 0,
    listeners: new Set(),    // (fraction, label)
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

function onWorkerMessage(event) {
    const msg = event.data ?? {};
    if (msg.type === 'progress') {
        // Only the weights count: the JSON configs finish first and would read as 90% done.
        if (msg.total < 1_000_000) return;
        model.files.set(msg.file, [msg.loaded, msg.total]);
        let loaded = 0, total = 0;
        for (const [l, t] of model.files.values()) { loaded += l; total += t; }
        const fraction = 0.9 * Math.min(1, loaded / total);
        // Monotonic, and at most one report per percent: the callback fires per network chunk.
        if (fraction >= model.reported + 0.01 || (fraction >= 0.9 && model.reported < 0.9)) {
            model.reported = fraction;
            broadcast(fraction, 'downloading');
        }
        return;
    }
    if (msg.type === 'estimating') {
        broadcast(0.92, 'estimating');
        return;
    }
    const job = model.jobs.get(msg.id);
    if (!job) return;
    model.jobs.delete(msg.id);
    if (msg.type === 'error') {
        console.warn('[depth-diorama] model unavailable; using heuristic depth', msg.message);
        model.failed = true;
        job.resolve(null);
    } else {
        job.resolve(msg);
    }
}

function getWorker() {
    if (!model.worker) {
        const url = URL.createObjectURL(new Blob([WORKER_SOURCE], { type: 'text/javascript' }));
        model.files.clear();
        model.reported = 0;
        model.worker = new Worker(url, { type: 'module' });
        URL.revokeObjectURL(url);
        model.worker.onmessage = onWorkerMessage;
        model.worker.onerror = (e) => {
            e.preventDefault?.();
            console.warn('[depth-diorama] model worker failed', e.message);
            model.failed = true;
            disposeWorker();
        };
    }
    return model.worker;
}

function disposeWorker() {
    try { model.worker?.terminate(); } catch { /* gone */ }
    model.worker = null;
    for (const job of model.jobs.values()) job.resolve(null);
    model.jobs.clear();
}

/** Terminates the worker after a quiet spell, which frees its GPU memory; the weights stay cached. */
function scheduleDispose() {
    clearTimeout(model.idleTimer);
    model.idleTimer = setTimeout(() => {
        if (model.jobs.size === 0) disposeWorker();
    }, MODEL_IDLE_DISPOSE_MS);
}

function modelDepth(src, image, analysis) {
    if (!modelInflight.has(src)) {
        const job = (async () => {
            const dtype = await pickDtype();
            if (!dtype) {
                model.failed = true;
                return null;
            }
            const scale = MODEL_INPUT_SIZE / Math.max(image.naturalWidth, image.naturalHeight);
            const canvas = document.createElement('canvas');
            canvas.width = Math.max(14, Math.round(image.naturalWidth * scale));
            canvas.height = Math.max(14, Math.round(image.naturalHeight * scale));
            const ctx = canvas.getContext('2d', { willReadFrequently: true });
            ctx.drawImage(image, 0, 0, canvas.width, canvas.height);
            const pixels = ctx.getImageData(0, 0, canvas.width, canvas.height).data;
            const id = model.nextJob++;
            const reply = await new Promise((resolve) => {
                model.jobs.set(id, { resolve });
                getWorker().postMessage(
                    { id, dtype, width: canvas.width, height: canvas.height, data: pixels },
                    [pixels.buffer]);
            });
            if (!reply?.data || !reply.width) return null;
            const depth = combineModelDepth(reply, analysis);
            depthCache.set(src, depth);
            if (depthCache.size > CACHE_LIMIT) depthCache.delete(depthCache.keys().next().value);
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

    // Ink = thin dark strokes. A dark area thicker than a line is a fill (hair, a night sky) —
    // including its rim: "dark and near something light" alone would draw a false outline round
    // every dark fill, splitting a night sky from its clouds along a luminance contour. So a dark
    // pixel is ink only if no dark pixel within reach is deeper than a stroke's half-width.
    await pause();
    const fromLight = distanceField(light, W, H);
    const thick = new Uint8Array(N);
    for (let i = 0; i < N; i++) thick[i] = fromLight[i] > INK_HALF_WIDTH ? 1 : 0;
    const nearThick = distanceField(thick, W, H);
    const ink = new Uint8Array(N);
    for (let i = 0; i < N; i++) ink[i] = !light[i] && nearThick[i] > INK_HALF_WIDTH + 1 ? 1 : 0;

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
    const reg = { area: [], minX: [], minY: [], maxX: [], maxY: [], lum: [], sat: [], r: [], g: [], b: [] };
    for (let i = 0; i < N; i++) {
        if (ink[i]) continue;
        const root = find(parent, i);
        let k = rootIndex[root];
        if (k < 0) {
            k = rootIndex[root] = reg.area.length;
            reg.area.push(0); reg.lum.push(0); reg.sat.push(0);
            reg.r.push(0); reg.g.push(0); reg.b.push(0);
            reg.minX.push(W); reg.minY.push(H); reg.maxX.push(0); reg.maxY.push(0);
        }
        const x = i % W, y = (i / W) | 0;
        label[i] = k;
        reg.area[k]++;
        reg.lum[k] += lum[i];
        reg.r[k] += R[i];
        reg.g[k] += G[i];
        reg.b[k] += B[i];
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
        reg.r[k] /= reg.area[k];
        reg.g[k] /= reg.area[k];
        reg.b[k] /= reg.area[k];
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

    // The frame = gutter + the dark border drawn against it (dark, not just ink: where a dark
    // backdrop meets the border the two read as one thick fill, and the border must still pin).
    const frame = new Uint8Array(N);
    if (anyGutter) {
        const fromGutter = distanceField(gutter, W, H);
        const borderReach = Math.max(3, W * 0.02);
        for (let i = 0; i < N; i++) frame[i] = gutter[i] || (!light[i] && fromGutter[i] <= borderReach) ? 1 : 0;
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
    // Classify each region within its panel — found from any pixel of it outside the frame (its
    // first pixel may be one the frame claimed).
    const regionPanel = new Int32Array(regions).fill(-1);
    for (let i = 0; i < N; i++) if (label[i] >= 0 && panelOf[i] >= 0 && regionPanel[label[i]] < 0) regionPanel[label[i]] = panelOf[i];
    const kind = new Uint8Array(regions);          // 0 object, 1 sky, 2 floor, 3 glass
    for (let k = 0; k < regions; k++) {
        if (reg.area[k] < minArea || isGutter[k]) continue;
        const p = regionPanel[k];
        if (p < 0) continue;
        const panel = panels[p];
        const pw = panel.maxX - panel.minX + 1, ph = panel.maxY - panel.minY + 1;
        const bw = reg.maxX[k] - reg.minX[k] + 1, bh = reg.maxY[k] - reg.minY[k] + 1;
        const topRel = (reg.minY[k] - panel.minY) / ph;
        const fill = reg.area[k] / (bw * bh);
        // ComicTextOverlayService letters every caption on the same pale yellow (255,255,180).
        const captionInk = reg.r[k] > 0.9 && reg.g[k] > 0.9 && reg.b[k] > 0.55 && reg.b[k] < 0.82;
        if ((captionInk || (reg.lum[k] > 0.8 && reg.sat[k] < 0.45 && fill > 0.3))
            && bw > bh * 1.6 && topRel < 0.38 && reg.area[k] < panel.area * 0.25) {
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
                height[i] = 0.5 + 0.26 * bottom + 0.11 * size * swell;
            }
        }
    }

    await pause();
    const rgb = new Float32Array(3 * N);
    for (let i = 0; i < N; i++) {
        rgb[3 * i] = R[i];
        rgb[3 * i + 1] = G[i];
        rgb[3 * i + 2] = B[i];
    }
    const analysis = { width: W, height: H, rgb, label, kind, frame, panelOf, panels: panels.length };
    Object.assign(analysis, assemble(height, analysis), { source: 'heuristic' });
    await pause();
    analysis.back = buildBackdrop(analysis.data, analysis);
    return analysis;
}

/**
 * From per-region heights to the finished field: outlines take the height of the nearer shape
 * they separate (a max filter over assigned neighbours, applied to ink only, so a line never
 * drags a whole ink network forward), the frame goes on the glass, and `fg` marks what stands
 * proud of the backdrop (objects, captions, the frame, and the ink that went with them) — the
 * shader looks behind exactly these. `height` is -1 where unassigned, and is consumed.
 */
function assemble(height, { width: W, height: H, label, kind, frame }) {
    const N = W * H;
    const fg = new Uint8Array(N);
    for (let i = 0; i < N; i++) fg[i] = frame[i] || (label[i] >= 0 && (kind[label[i]] === 0 || kind[label[i]] === 3)) ? 1 : 0;
    for (let pass = 0; pass < 3; pass++) {
        const src = height.slice();
        const srcFg = fg.slice();
        for (let y = 0; y < H; y++) {
            for (let x = 0; x < W; x++) {
                const i = y * W + x;
                if (src[i] >= 0 || frame[i]) continue;
                let best = -1, bestFg = 0;
                for (let dy = -2; dy <= 2; dy++) {
                    const yy = y + dy;
                    if (yy < 0 || yy >= H) continue;
                    for (let dx = -2; dx <= 2; dx++) {
                        const xx = x + dx;
                        if (xx < 0 || xx >= W) continue;
                        const j = yy * W + xx;
                        if (src[j] > best) {
                            best = src[j];
                            bestFg = srcFg[j];
                        }
                    }
                }
                if (best >= 0) {
                    height[i] = best;
                    fg[i] = bestFg;
                }
            }
        }
    }

    const pin = new Uint8Array(N);
    for (let i = 0; i < N; i++) {
        if (frame[i]) height[i] = 1;
        else if (height[i] < 0) height[i] = 0.3;
        pin[i] = height[i] >= 0.999 ? 1 : 0;
    }
    // One pixel of margin, so the anti-aliased fringe of an outline counts as covered too.
    const fgWide = new Uint8Array(N);
    for (let y = 0; y < H; y++) {
        for (let x = 0; x < W; x++) {
            const i = y * W + x;
            fgWide[i] = fg[i] || (x > 0 && fg[i - 1]) || (x < W - 1 && fg[i + 1])
                || (y > 0 && fg[i - W]) || (y < H - 1 && fg[i + W]) ? 255 : 0;
        }
    }
    return { data: finish(height, pin, W, H), fg: fgWide, pin };
}

// ── The layer behind the cut-outs ─────────────────────────────────────────────────────────
//
// A height field can only extrude: tilted, a character's silhouette becomes a wall running down
// to the backdrop, textured with its outline smeared across the jump. A diorama card is thin, so
// what really comes into view there is the backdrop BEHIND it. This builds that layer: the
// backdrop's own height, with the cut-outs (the `fg` mask) painted out by a push-pull fill from
// the backdrop around them. The shader marches it separately.

/**
 * Push-pull hole filling: average the known samples down a pyramid, then fill each unknown
 * pixel from the level above. `values` is C channels per pixel; `weight` 1 known, 0 unknown.
 */
function pushPull(values, weight, W, H, C) {
    const levels = [];
    let P = new Float32Array(values.length);
    for (let i = 0; i < weight.length; i++) for (let c = 0; c < C; c++) P[i * C + c] = values[i * C + c] * weight[i];
    let A = Float32Array.from(weight);
    let w = W, h = H;
    levels.push({ w, h, P, A });
    while (w > 1 || h > 1) {
        const w2 = Math.ceil(w / 2), h2 = Math.ceil(h / 2);
        const P2 = new Float32Array(w2 * h2 * C), A2 = new Float32Array(w2 * h2);
        for (let y = 0; y < h2; y++) {
            for (let x = 0; x < w2; x++) {
                const o = y * w2 + x;
                let n = 0;
                for (let dy = 0; dy < 2; dy++) {
                    const yy = 2 * y + dy;
                    if (yy >= h) continue;
                    for (let dx = 0; dx < 2; dx++) {
                        const xx = 2 * x + dx;
                        if (xx >= w) continue;
                        const i = yy * w + xx;
                        n++;
                        A2[o] += A[i];
                        for (let c = 0; c < C; c++) P2[o * C + c] += P[i * C + c];
                    }
                }
                A2[o] /= n;
                for (let c = 0; c < C; c++) P2[o * C + c] /= n;
            }
        }
        w = w2; h = h2; P = P2; A = A2;
        levels.push({ w, h, P, A });
    }
    const top = levels[levels.length - 1];
    for (let c = 0; c < C; c++) top.P[c] = top.A[0] > 0 ? top.P[c] / top.A[0] : 0;
    for (let l = levels.length - 2; l >= 0; l--) {
        const fine = levels[l], coarse = levels[l + 1];
        for (let y = 0; y < fine.h; y++) {
            for (let x = 0; x < fine.w; x++) {
                const i = y * fine.w + x;
                const o = (y >> 1) * coarse.w + (x >> 1);
                // P is premultiplied by coverage A (0..1), so the gap is exactly 1 - A.
                const missing = 1 - fine.A[i];
                for (let c = 0; c < C; c++) fine.P[i * C + c] += missing * coarse.P[o * C + c];
            }
        }
    }
    return levels[0].P;
}

/** RGBA8 at the analysis size: rgb = backdrop colour with the cut-outs painted out, a = its height. */
function buildBackdrop(depth, analysis) {
    const { width: W, height: H, rgb, panelOf, fg } = analysis;
    const N = W * H;
    const weight = new Float32Array(N);
    const values = new Float32Array(4 * N);
    for (let i = 0; i < N; i++) {
        weight[i] = panelOf[i] >= 0 && !fg[i] ? 1 : 0;
        values[4 * i] = rgb[3 * i];
        values[4 * i + 1] = rgb[3 * i + 1];
        values[4 * i + 2] = rgb[3 * i + 2];
        values[4 * i + 3] = depth[i] / 255;
    }
    const filled = pushPull(values, weight, W, H, 4);
    const out = new Uint8Array(4 * N);
    for (let c = 0; c < 4; c++) {
        const channel = new Float32Array(N);
        for (let i = 0; i < N; i++) channel[i] = filled[4 * i + c];
        const soft = boxBlur(channel, W, H, c === 3 ? 2 : 1);
        for (let i = 0; i < N; i++) {
            // Known backdrop keeps its own colour; only the painted-out holes are softened.
            const v = c < 3 && weight[i] ? channel[i] : soft[i];
            out[4 * i + c] = Math.round(Math.min(1, Math.max(0, v)) * 255);
        }
    }
    return { width: W, height: H, data: out };
}

/**
 * Grows every raised shape by a texel before a light anti-aliasing blur, then re-pins the glass
 * so borders and captions stay crisp. The growth matters: without it the blur's slope starts
 * inside the outline, and the far side of a slope is hidden from an oblique view — so every
 * card lost the outline on the side facing away from the viewer.
 */
function finish(height, pin, W, H) {
    const grown = new Float32Array(height.length);
    for (let y = 0; y < H; y++) {
        for (let x = 0; x < W; x++) {
            let v = 0;
            for (let dy = -1; dy <= 1; dy++) {
                const yy = Math.min(H - 1, Math.max(0, y + dy));
                for (let dx = -1; dx <= 1; dx++) v = Math.max(v, height[yy * W + Math.min(W - 1, Math.max(0, x + dx))]);
            }
            grown[y * W + x] = v;
        }
    }
    const soft = boxBlur(grown, W, H, 1);
    const out = new Uint8Array(W * H);
    for (let i = 0; i < out.length; i++) out[i] = pin[i] ? 255 : Math.round(Math.min(1, Math.max(0, soft[i])) * 255);
    return out;
}

/**
 * Model depth, guided by the segmentation. A depth network's edges are soft, and soft edges
 * under parallax are a rubber sheet: shapes skew instead of sliding. So the model decides each
 * cut-out's PLACE (its mean depth) and a little of its relief, while the regions' own outlines
 * decide where one surface ends; the backdrop takes the model's depth as-is. Normalised per panel
 * (2nd–98th percentile), so one deep panel does not flatten the other.
 */
function combineModelDepth(raw, analysis) {
    const MW = raw.width, MH = raw.height, channels = raw.channels ?? 1;
    const { width: W, height: H, label, kind, frame, pin, panelOf, panels } = analysis;
    const N = W * H;
    // Box-average the model's output down to the analysis grid.
    const m = new Float32Array(N);
    const count = new Float32Array(N);
    for (let y = 0; y < MH; y++) {
        const row = Math.min(H - 1, Math.floor(y * H / MH)) * W;
        for (let x = 0; x < MW; x++) {
            const j = row + Math.min(W - 1, Math.floor(x * W / MW));
            m[j] += raw.data[(y * MW + x) * channels];
            count[j]++;
        }
    }
    for (let j = 0; j < N; j++) m[j] /= Math.max(1, count[j]);

    const hist = Array.from({ length: Math.max(1, panels) }, () => new Uint32Array(256));
    for (let j = 0; j < N; j++) if (panelOf[j] >= 0 && !pin[j]) hist[panelOf[j]][Math.round(m[j])]++;
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
    for (let j = 0; j < N; j++) {
        const [lo, hi] = range[Math.max(0, panelOf[j])];
        m[j] = Math.min(1, Math.max(0, (m[j] - lo) / (hi - lo)));
    }

    const sum = new Float32Array(kind.length), area = new Float32Array(kind.length);
    for (let j = 0; j < N; j++) {
        if (label[j] >= 0) {
            sum[label[j]] += m[j];
            area[label[j]]++;
        }
    }
    const height = new Float32Array(N).fill(-1);
    for (let j = 0; j < N; j++) {
        const k = label[j];
        if (k < 0 || frame[j] || panelOf[j] < 0) continue;
        if (kind[k] === 3) {
            height[j] = 1;
        } else if (kind[k] === 0) {
            const mean = sum[k] / area[k];
            height[j] = 0.06 + 0.84 * (mean + 0.6 * Math.max(-0.12, Math.min(0.12, m[j] - mean)));
        } else {
            height[j] = 0.04 + 0.8 * m[j];
        }
    }
    const result = { width: W, height: H, ...assemble(height, analysis), source: 'model' };
    result.back = buildBackdrop(result.data, { ...analysis, fg: result.fg });
    return result;
}

// ── Rendering ─────────────────────────────────────────────────────────────────────────────

const FRAGMENT_SHADER = `#version 300 es
precision highp float;
in vec2 vUv;
out vec4 outColor;

uniform sampler2D uImage;     // FLIP_Y applied: origin bottom-left, like vUv
uniform sampler2D uDepthA;    // r = height (1 = glass), g = stands proud of the backdrop; rows top-down
uniform sampler2D uDepthB;
uniform sampler2D uBackA;     // rgb = backdrop with the cut-outs painted out, a = its height
uniform sampler2D uBackB;
uniform float uMix;           // A -> B morph
uniform vec2  uTilt;          // -1..1; +x viewer to the right, +y viewer above
uniform vec2  uShift;         // uv shift of the deepest point at |tilt| = 1
uniform vec2  uImageSize;
uniform vec2  uDepthTexel;
uniform float uAmount;        // intro/outro 0..1
uniform float uDebug;         // 0 render, 1 height, 2 backdrop colour, 3 backdrop height
uniform vec3  uRim;
uniform vec3  uShade;

vec2 rows(vec2 uv) { return vec2(uv.x, 1.0 - uv.y); }

// Every lookup names its LOD. Inside the march, neighbouring pixels stop at different steps, so
// implicit derivatives are garbage and pick a coarse mip: blocky, straight-edged silhouettes.
float heightAt(vec2 uv) {
    return mix(textureLod(uDepthA, rows(uv), 0.0).r, textureLod(uDepthB, rows(uv), 0.0).r, uMix);
}

float heightLod(vec2 uv, float lod) {
    return mix(textureLod(uDepthA, rows(uv), lod).r, textureLod(uDepthB, rows(uv), lod).r, uMix);
}

float coveredAt(vec2 uv) {
    return mix(textureLod(uDepthA, rows(uv), 0.0).g, textureLod(uDepthB, rows(uv), 0.0).g, uMix);
}

vec4 backAt(vec2 uv) {
    return mix(textureLod(uBackA, rows(uv), 0.0), textureLod(uBackB, rows(uv), 0.0), uMix);
}

void main() {
    if (uDebug > 0.5) {
        vec4 b = backAt(vUv);
        outColor = vec4(uDebug < 1.5 ? vec3(heightAt(vUv)) : uDebug < 2.5 ? b.rgb : vec3(b.a), 1.0);
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
    // Refinement: bisect between the last sample above the surface and the first below it.
    // A linear-interpolated hit is fine on smooth relief, but the cut-outs are steps, and on a
    // step it lands on the outline for whole bands of pixels: ghost outlines, one per layer.
    if (rayD > 0.0) {
        vec2 lo = uv - stepUv, hi = uv;
        float loD = rayD - stepD, hiD = rayD;
        for (int i = 0; i < 5; i++) {
            vec2 mid = 0.5 * (lo + hi);
            float midD = 0.5 * (loD + hiD);
            if (midD >= 1.0 - heightAt(mid)) { hi = mid; hiD = midD; } else { lo = mid; loD = midD; }
        }
        uv = hi;
    }

    // Normal from the height field's own slope, over two texels so 8-bit steps do not band.
    vec2 tx = vec2(uDepthTexel.x * 2.0, 0.0), ty = vec2(0.0, uDepthTexel.y * 2.0);
    float hc = heightAt(uv);
    float hl = heightAt(uv - tx), hr = heightAt(uv + tx), hd = heightAt(uv - ty), hu = heightAt(uv + ty);
    float dx = hr - hl, dy = hu - hd;
    vec3 N = normalize(vec3(-dx * 3.0, -dy * 3.0, 1.0));
    float ramp = 1.0 - N.z;

    // The backdrop layer, for where the ray slipped past a cut-out's edge. It is smooth, so a
    // few fixed-point iterations find where the ray meets it.
    vec2 uvB = vUv;
    for (int i = 0; i < 4; i++) uvB = vUv - S * (1.0 - backAt(uvB).a);
    vec4 back = backAt(uvB);

    // The march stopping part-way down a steep edge means it grazed the side of a cut-out: a
    // thin card has no side, so that pixel shows what is behind it instead. Only part-way down:
    // the top of the edge is the card's own outline, and must stay with the card.
    float hmax = max(max(hl, hr), max(hd, hu));
    float inside = min(hc - min(min(hl, hr), min(hd, hu)), hmax - hc);
    // On a card's face, not on the foot of a jump: only there does the relief get lit.
    float onFace = (1.0 - smoothstep(0.015, 0.05, hmax - hc));
    // And only where the ray ran INTO the edge: a ray running along a border never meets its side.
    float into = dot(normalize(vec2(dx, dy) + 1e-6), -normalize(S + 1e-6));
    float wall = smoothstep(0.2, 0.4, ramp) * smoothstep(0.02, 0.07, inside) * smoothstep(0.15, 0.5, into)
        * smoothstep(0.0, 0.15, uAmount);
    float h = mix(hc, back.a, wall);
    vec2 at = mix(uv, uvB, wall);
    float depth = 1.0 - h;
    // Shading reads only the gentle curvature of the inflated shapes, never the jumps.
    N = normalize(mix(N, vec3(0.0, 0.0, 1.0), smoothstep(0.06, 0.16, ramp)));
    float slope = 1.0 - N.z;

    // Key light from the upper left, as comics are drawn, leaning a little with the view.
    vec3 L = normalize(vec3(-0.4 + tilt.x * 0.2, 0.5 + tilt.y * 0.2, 1.1));
    vec3 V = normalize(vec3(tilt * 0.6, 1.0));
    float shade = mix(1.0, clamp(dot(N, L) / L.z, 0.75, 1.15), onFace * (1.0 - wall));

    // Explicit LOD: derivatives of the displaced uv spike at occlusion edges and would pop mips.
    vec2 texelsPerPixel = fwidth(vUv) * uImageSize;
    float baseLod = max(0.0, log2(max(texelsPerPixel.x, texelsPerPixel.y)));
    float dof = 1.3 * smoothstep(0.55, 1.0, depth) * clamp(length(tilt) * 1.6 + 0.3, 0.0, 1.0);
    vec3 front = textureLod(uImage, uv, baseLod + dof).rgb;
    vec3 color = front;
    if (wall > 0.0) {
        // What comes into view beside a card was under it in the source. The backdrop it
        // continues is the nearest uncovered pixel toward the viewer's side, which is exactly
        // the edge the card is sliding away from; the painted-in layer is the last resort.
        vec2 q = uvB;
        vec2 search = normalize(S + 1e-6) * uDepthTexel * 1.5;
        float covered = coveredAt(q);
        for (int i = 0; i < 14; i++) {
            if (covered < 0.5) break;
            q += search;
            covered = coveredAt(q);
        }
        vec3 behind = back.rgb;
        if (covered < 0.5) {
            // Bisect to the card's true edge, so neighbouring pixels sample neighbouring texels
            // rather than snapping to whole search steps (which draws contour rings), then step
            // past the outline's anti-aliased fringe.
            vec2 lo = q - search, hi = q;
            for (int i = 0; i < 4; i++) {
                vec2 mid = 0.5 * (lo + hi);
                if (coveredAt(mid) < 0.5) hi = mid; else lo = mid;
            }
            behind = textureLod(uImage, hi + search * 1.6, baseLod + dof).rgb;
        }
        color = mix(front, behind, wall);
    }

    // Cast shadow from thin cards. A card at height hc shades the point whose light ray reaches
    // hc exactly where the card is, i.e. at offset (hc - h) toward the light — so find a t with
    // height(at + span * t) - h == t by fixed-point iteration, and shade only if it is consistent.
    // A point under a card's edge (seen beside it) is correctly left lit: its ray clears the
    // edge before it climbs to the card. Continuous in the pixel, so no tap rings.
    vec2 span = normalize(L.xy) * length(L.xy) / L.z * uShift;
    float t = clamp(heightLod(at + span * 0.5, 3.0) - h, 0.0, 1.0);
    t = clamp(heightLod(at + span * t, 2.0) - h, 0.0, 1.0);
    float occ = heightLod(at + span * t, 1.5) - h;
    float shadow = smoothstep(0.06, 0.2, occ) * (1.0 - smoothstep(0.1, 0.3, abs(occ - t)));
    // A little crease occlusion where a surface dips below its surroundings (not on what is
    // seen behind a card: in the source that point is under the card, not beside it).
    float ao = smoothstep(0.05, 0.35, heightLod(at, 2.0) - h) * (1.0 - wall) * step(0.5, coveredAt(uv));
    float darken = clamp(shadow * 0.3 + ao * 0.18, 0.0, 0.55) * uAmount;

    color = mix(color, color * shade, 0.7 * uAmount);
    color = mix(color, color * uShade, darken);

    // Rim light on the rounded parts that face the light, and a faint gloss sweeping with the tilt.
    float facing = max(dot(normalize(N.xy + 1e-4), normalize(L.xy)), 0.0);
    float rim = smoothstep(0.02, 0.08, slope) * facing * onFace * (1.0 - wall);
    float gloss = pow(max(dot(N, normalize(L + V)), 0.0), 40.0) * onFace * (1.0 - wall);
    color += uRim * (rim * 0.22 + gloss * 0.07) * uAmount;

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

function upload(gl, texture, width, height, format, internal, data) {
    gl.bindTexture(gl.TEXTURE_2D, texture);
    gl.pixelStorei(gl.UNPACK_ALIGNMENT, 1);
    gl.pixelStorei(gl.UNPACK_FLIP_Y_WEBGL, false);
    gl.texImage2D(gl.TEXTURE_2D, 0, internal, width, height, 0, format, gl.UNSIGNED_BYTE, data);
    gl.generateMipmap(gl.TEXTURE_2D);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR_MIPMAP_LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
    gl.pixelStorei(gl.UNPACK_ALIGNMENT, 4);
    gl.bindTexture(gl.TEXTURE_2D, null);
}

/** One depth "slot": the height field and the backdrop layer built from it. */
function createLayers(gl, depth) {
    const layers = { height: gl.createTexture(), back: gl.createTexture(), size: [depth.width, depth.height] };
    const rg = new Uint8Array(depth.data.length * 2);
    for (let i = 0; i < depth.data.length; i++) {
        rg[2 * i] = depth.data[i];
        rg[2 * i + 1] = depth.fg[i];
    }
    upload(gl, layers.height, depth.width, depth.height, gl.RG, gl.RG8, rg);
    upload(gl, layers.back, depth.back.width, depth.back.height, gl.RGBA, gl.RGBA8, depth.back.data);
    return layers;
}

/**
 * Puts the canvas exactly over the img's content box. Size from the used width/height (they keep
 * their fractions); position from the client rects when nothing is transformed (sub-pixel exact,
 * and right for a grid-centred img, whose auto margins computed style reports as 0), else from
 * offsetLeft/Top — the scroll tilt and the entrance scale distort client rects but not layout.
 */
function align(canvas, img, container) {
    try {
        const cs = getComputedStyle(img);
        const bl = parseFloat(cs.borderLeftWidth) || 0, bt = parseFloat(cs.borderTopWidth) || 0;
        const ir = img.getBoundingClientRect(), cr = container.getBoundingClientRect();
        const flat = Math.abs(cr.width - container.offsetWidth) < 0.01 && Math.abs(cr.height - container.offsetHeight) < 0.01;
        const left = flat ? ir.left - cr.left - container.clientLeft : img.offsetLeft;
        const top = flat ? ir.top - cr.top - container.clientTop : img.offsetTop;
        canvas.style.left = `${left + bl}px`;
        canvas.style.top = `${top + bt}px`;
        canvas.style.width = `${Math.max(0, parseFloat(cs.width) - bl - (parseFloat(cs.borderRightWidth) || 0))}px`;
        canvas.style.height = `${Math.max(0, parseFloat(cs.height) - bt - (parseFloat(cs.borderBottomWidth) || 0))}px`;
    } catch {
        // Detached; the frame task tears down.
    }
}

const smooth = (t) => t * t * (3 - 2 * t);

const DEBUG_VIEWS = { depth: 1, backdrop: 2, 'backdrop-depth': 3 };

function release(gl, layers) {
    try {
        gl.deleteTexture(layers.height);
        gl.deleteTexture(layers.back);
    } catch { /* context gone */ }
}

// ── Public API ────────────────────────────────────────────────────────────────────────────

/** WebGL2 present and the effects tier at least `lite`. */
export function supported() {
    try {
        return gfx.describe().webgl2 && gfx.allows('lite');
    } catch {
        return false;
    }
}

/** Image + heuristic analysis for a src, once; a failure is forgotten so a later call retries. */
function analysisFor(src) {
    if (!analysisCache.has(src)) {
        const job = loadImage(src, 10000)
            .then(async (image) => (image ? { image, analysis: await analyse(image) } : null))
            .catch(() => null)
            .then((result) => {
                if (!result) analysisCache.delete(src);
                return result;
            });
        analysisCache.set(src, job);
        // Each entry holds a decoded image: keep the last few comics, not the whole session.
        if (analysisCache.size > CACHE_LIMIT) analysisCache.delete(analysisCache.keys().next().value);
    }
    return analysisCache.get(src);
}

/**
 * Computes the heuristic depth ahead of time — call it when the strip's image has loaded and the
 * page is idle — so the first tap on "3D" starts at once instead of after ~0.3s of analysis.
 * Resolves true when the depth is ready. Does nothing below the `lite` tier.
 */
export async function prepare(src) {
    try {
        return !!src && supported() && !!(await analysisFor(src));
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
 *           onEnd?: (reason: string) => void, preferModel?: boolean,
 *           debug?: 'depth' | 'backdrop' | 'backdrop-depth' | true }} options
 * @returns {Promise<number>} handle, or 0 when nothing started (the plain img stays as it is)
 */
export async function start(container, options = {}) {
    try {
        // iOS asks for motion permission, and only inside a user gesture: so first, before any
        // await. The listener goes on regardless; refused, it just never hears anything.
        try {
            DeviceOrientationEvent?.requestPermission?.().catch(() => {});
        } catch { /* not in a gesture: pointer and idle orbit still work */ }

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
        const began = performance.now();
        const prepared = await analysisFor(src);
        if (options.debug) console.debug(`[depth-diorama] heuristic depth ready in ${(performance.now() - began).toFixed(1)}ms`);
        if (!prepared || !container.isConnected || !supported()) return 0;
        const { image, analysis } = prepared;
        const cached = depthCache.get(src);
        const initial = cached ?? analysis;

        const canvas = document.createElement('canvas');
        canvas.className = 'depth-diorama-canvas';
        canvas.setAttribute('aria-hidden', 'true');
        Object.assign(canvas.style, { position: 'absolute', display: 'block', pointerEvents: 'none' });
        align(canvas, img, container);
        container.appendChild(canvas);

        const surface = createSurface(canvas, { maxDpr: MAX_DPR });
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
            image: u('uImage'), depthA: u('uDepthA'), depthB: u('uDepthB'), backA: u('uBackA'), backB: u('uBackB'),
            mix: u('uMix'), tilt: u('uTilt'),
            shift: u('uShift'), imageSize: u('uImageSize'), depthTexel: u('uDepthTexel'), amount: u('uAmount'),
            debug: u('uDebug'), rim: u('uRim'), shade: u('uShade')
        };
        const vao = gl.createVertexArray();
        const imageTex = createTexture(gl, image, { mipmaps: true });
        const layersA = createLayers(gl, initial);

        const id = nextId++;
        const now = performance.now();
        const inst = {
            container, canvas, img, surface, gl, program, loc, vao, imageTex,
            layersA, layersB: null,
            morphFrom: 0,                 // performance.now() of a running A->B morph, or 0
            startedAt: now,
            endingAt: 0,
            visible: true,
            stopTask: null,
            listeners: [],
            observers: [],
            palette: readPalette(),
            tilt: [0, 0], tiltMid: [0, 0], input: [0, 0], inputWeight: 0, lastInputAt: -1e9,
            scale: 1, slowFor: 0, fastFor: 0,
            orient: null,
            debug: DEBUG_VIEWS[options.debug] ?? 0,
            imageSize: [image.naturalWidth, image.naturalHeight],
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
        if (inst.layersB) release(gl, inst.layersB);
        inst.layersB = createLayers(gl, depth);
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
            // Settle: B becomes A, so the steady state binds one set.
            release(inst.gl, inst.layersA);
            inst.layersA = inst.layersB;
            inst.layersB = null;
            inst.morphFrom = 0;
            mix = 0;
        }
    }

    // The pass is fill-rate bound, so resolution is the lever: shed it after sustained slow
    // frames (well before gfx-core's 1.5s tier drop would end the effect), regain it slowly.
    inst.slowFor = dt > 0.024 ? inst.slowFor + dt : 0;
    inst.fastFor = dt < 0.018 ? inst.fastFor + dt : 0;
    if (inst.slowFor > 0.6 && inst.scale > 0.5) {
        inst.scale = Math.max(0.5, inst.scale * 0.8);
        inst.slowFor = 0;
    } else if (inst.fastFor > 4 && inst.scale < 1) {
        inst.scale = Math.min(1, inst.scale * 1.15);
        inst.fastFor = 0;
    }

    const { surface, gl, loc, canvas } = inst;
    if (surface.dead) return;
    const dpr = Math.min(window.devicePixelRatio || 1, MAX_DPR) * inst.scale;
    const width = Math.max(1, Math.round(canvas.clientWidth * dpr));
    const height = Math.max(1, Math.round(canvas.clientHeight * dpr));
    if (canvas.width !== width || canvas.height !== height) {
        canvas.width = width;
        canvas.height = height;
    }
    gl.bindFramebuffer(gl.FRAMEBUFFER, null);
    gl.viewport(0, 0, width, height);
    const aspect = width / Math.max(1, height);
    const shift = aspect >= 1 ? [PARALLAX / aspect, PARALLAX] : [PARALLAX, PARALLAX * aspect];

    gl.useProgram(inst.program);
    gl.bindVertexArray(inst.vao);
    gl.activeTexture(gl.TEXTURE0);
    gl.bindTexture(gl.TEXTURE_2D, inst.imageTex);
    const A = inst.layersA, B = inst.layersB ?? inst.layersA;
    const textures = [inst.imageTex, A.height, B.height, A.back, B.back];
    for (let k = 1; k < textures.length; k++) {
        gl.activeTexture(gl.TEXTURE0 + k);
        gl.bindTexture(gl.TEXTURE_2D, textures[k]);
    }
    gl.uniform1i(loc.image, 0);
    gl.uniform1i(loc.depthA, 1);
    gl.uniform1i(loc.depthB, 2);
    gl.uniform1i(loc.backA, 3);
    gl.uniform1i(loc.backB, 4);
    gl.uniform1f(loc.mix, mix);
    gl.uniform2f(loc.tilt, inst.tilt[0], inst.tilt[1]);
    gl.uniform2f(loc.shift, shift[0], shift[1]);
    gl.uniform2f(loc.imageSize, inst.imageSize[0], inst.imageSize[1]);
    gl.uniform2f(loc.depthTexel, 1 / Math.max(A.size[0], B.size[0]), 1 / Math.max(A.size[1], B.size[1]));
    gl.uniform1f(loc.amount, amount);
    gl.uniform1f(loc.debug, inst.debug);
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
