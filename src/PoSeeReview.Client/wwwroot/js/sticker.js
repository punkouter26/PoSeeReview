// Comic stickers: the payoff panel's characters, cut out in the browser and die-cut in white.
//
// A matting model (MODNet) runs on the visitor's device through transformers.js
// in a Blob module worker — the same runtime and the same reasoning as depth-diorama.js: the
// session build would stall the shared frame loop on the main thread. WebGPU with fp16 weights
// (13MB) where the adapter has `shader-f16`, otherwise WebAssembly with fp32 weights (26MB). Weights
// are cached by the browser after the first sticker. Nothing is uploaded anywhere.
//
// The panel is cropped below its caption box (ComicTextOverlayService draws one across the top of
// every panel), matted, then given a white outline and a soft shadow on a transparent canvas, and
// handed back as a PNG blob for the page to download. Resolves null on any failure; never throws.

import { comicImageUrl, loadImage } from './gfx-core.js';

const TRANSFORMERS_URL = 'https://cdn.jsdelivr.net/npm/@huggingface/transformers@4.3.1/dist/transformers.min.js';
// MODNet is a portrait matting model, and the characters in a comic are people. Tried first:
// BRIA RMBG-1.4 (its Segformer config is refused by the 4.3.1 pipeline) and ormbg, which mats
// every ink line as foreground on flat cartoon art.
const MODEL_ID = 'Xenova/modnet';
const DEADLINE_MS = 120000;
const CAPTION_SHARE = 0.2;      // the top fifth of a panel is its caption box
const MAX_SIDE = 1024;
const OUTLINE = 10;             // px of white die-cut border
const PAD = OUTLINE * 3;
const ALPHA_LO = 100;           // matte confidence below this is background
const ALPHA_HI = 150;           // and above this, subject

const WORKER_SOURCE = `
let loading = null;
self.onmessage = async (event) => {
    const msg = event.data;
    try {
        loading ??= (async () => {
            const tf = await import(${JSON.stringify(TRANSFORMERS_URL)});
            tf.env.allowLocalModels = false;
            tf.env.useBrowserCache = true;
            const pipe = await tf.pipeline('background-removal', ${JSON.stringify(MODEL_ID)}, { device: msg.device, dtype: msg.dtype });
            return { pipe, RawImage: tf.RawImage };
        })();
        const { pipe, RawImage } = await loading;
        const result = await pipe(new RawImage(msg.data, msg.width, msg.height, 4));
        const out = Array.isArray(result) ? result[0] : result;
        const rgba = out.channels === 4 ? out : out.rgba();
        const data = new Uint8ClampedArray(rgba.data);
        self.postMessage({ id: msg.id, width: rgba.width, height: rgba.height, data }, [data.buffer]);
    } catch (err) {
        loading = null;
        self.postMessage({ id: msg.id, error: String(err && err.message || err) });
    }
};`;

let worker = null;
let nextJob = 1;
const jobs = new Map();

async function pickRuntime() {
    try {
        const adapter = await navigator.gpu?.requestAdapter();
        if (adapter?.features.has('shader-f16')) return { device: 'webgpu', dtype: 'fp16' };
    } catch { /* no WebGPU */ }
    // fp32, not q8: the quantised weights blur the matte edge, and 26MB is small enough.
    return { device: 'wasm', dtype: 'fp32' };
}

function getWorker() {
    if (!worker) {
        const url = URL.createObjectURL(new Blob([WORKER_SOURCE], { type: 'text/javascript' }));
        worker = new Worker(url, { type: 'module' });
        URL.revokeObjectURL(url);
        worker.onmessage = (event) => {
            const msg = event.data ?? {};
            const job = jobs.get(msg.id);
            if (!job) return;
            jobs.delete(msg.id);
            if (msg.error) console.warn('[sticker] background removal failed', msg.error);
            job.resolve(msg.error ? null : msg);
        };
        worker.onerror = (e) => {
            e.preventDefault?.();
            console.warn('[sticker] worker failed', e.message);
            try { worker?.terminate(); } catch { /* gone */ }
            worker = null;
            for (const job of jobs.values()) job.resolve(null);
            jobs.clear();
        };
    }
    return worker;
}

function cut(pixels, width, height, runtime) {
    const id = nextJob++;
    return new Promise((resolve) => {
        const timer = setTimeout(() => { if (jobs.delete(id)) resolve(null); }, DEADLINE_MS);
        jobs.set(id, { resolve: (value) => { clearTimeout(timer); resolve(value); } });
        try {
            getWorker().postMessage({ id, width, height, data: pixels, ...runtime }, [pixels.buffer]);
        } catch {
            jobs.delete(id);
            clearTimeout(timer);
            resolve(null);
        }
    });
}

/** The last panel, below its caption, scaled to at most MAX_SIDE. */
function cropPayoffPanel(image, panelCount) {
    // Unknown (older comics): a two-panel strip is drawn portrait, a single panel landscape.
    const panels = panelCount === 1 || panelCount === 2 ? panelCount : (image.naturalHeight > image.naturalWidth ? 2 : 1);
    const panelHeight = image.naturalHeight / panels;
    const top = panelHeight * (panels - 1) + panelHeight * CAPTION_SHARE;
    const height = panelHeight * (1 - CAPTION_SHARE);
    const scale = Math.min(1, MAX_SIDE / Math.max(image.naturalWidth, height));
    const canvas = document.createElement('canvas');
    canvas.width = Math.round(image.naturalWidth * scale);
    canvas.height = Math.round(height * scale);
    const ctx = canvas.getContext('2d', { willReadFrequently: true });
    ctx.drawImage(image, 0, top, image.naturalWidth, height, 0, 0, canvas.width, canvas.height);
    return ctx.getImageData(0, 0, canvas.width, canvas.height);
}

/**
 * A die-cut has a hard edge. Matting models trained on photos hedge on flat cartoon colour and
 * return a translucent haze over the whole panel; a narrow ramp around the midpoint keeps what
 * the model is sure of, drops the haze, and leaves a pixel or two of anti-aliasing.
 */
function hardenAlpha(data) {
    for (let i = 3; i < data.length; i += 4) {
        const a = data[i];
        data[i] = a >= ALPHA_HI ? 255 : a <= ALPHA_LO ? 0 : Math.round((a - ALPHA_LO) * 255 / (ALPHA_HI - ALPHA_LO));
    }
    return data;
}

/**
 * Keeps the subject and drops the confetti: the matte leaves specks wherever a background shape
 * resembles skin or cloth, and a die-cut outlines every one of them. Components smaller than a
 * quarter of the largest one (and than 1% of the panel) are cleared, and the rest bounded.
 */
function keepMainShapes(data, width, height) {
    const labels = new Int32Array(width * height);
    const sizes = [0];
    const stack = [];
    for (let start = 0; start < labels.length; start++) {
        if (labels[start] || data[start * 4 + 3] === 0) continue;
        const label = sizes.length;
        let size = 0;
        labels[start] = label;
        stack.push(start);
        while (stack.length) {
            const p = stack.pop();
            size++;
            const x = p % width;
            for (const q of [p - width, p + width, x > 0 ? p - 1 : -1, x < width - 1 ? p + 1 : -1]) {
                if (q >= 0 && q < labels.length && !labels[q] && data[q * 4 + 3] !== 0) {
                    labels[q] = label;
                    stack.push(q);
                }
            }
        }
        sizes.push(size);
    }

    // The kept shapes' bounding box, so the sticker is cropped to its subject. Null when nothing is kept.
    const floor = Math.max(Math.max(...sizes) / 4, labels.length * 0.01);
    let x0 = width, y0 = height, x1 = -1, y1 = -1;
    for (let p = 0; p < labels.length; p++) {
        if (!labels[p]) continue;
        if (sizes[labels[p]] < floor) {
            data[p * 4 + 3] = 0;
            continue;
        }
        const x = p % width, y = (p / width) | 0;
        x0 = Math.min(x0, x); x1 = Math.max(x1, x); y0 = Math.min(y0, y); y1 = Math.max(y1, y);
    }
    return x1 < 0 ? null : { x0, y0, x1, y1 };
}

/** White die-cut border and shadow around the matted cut-out. */
function dieCut(matte) {
    hardenAlpha(matte.data);
    const box = keepMainShapes(matte.data, matte.width, matte.height);
    if (!box) return null;
    const cutout = document.createElement('canvas');
    cutout.width = matte.width;
    cutout.height = matte.height;
    cutout.getContext('2d').putImageData(new ImageData(matte.data, matte.width, matte.height), 0, 0);

    // The silhouette, filled white, stamped around a circle: a cheap morphological dilation.
    const silhouette = document.createElement('canvas');
    silhouette.width = matte.width;
    silhouette.height = matte.height;
    const s = silhouette.getContext('2d');
    s.drawImage(cutout, 0, 0);
    s.globalCompositeOperation = 'source-in';
    s.fillStyle = '#ffffff';
    s.fillRect(0, 0, matte.width, matte.height);

    const out = document.createElement('canvas');
    out.width = box.x1 - box.x0 + 1 + PAD * 2;
    out.height = box.y1 - box.y0 + 1 + PAD * 2;
    const dx = PAD - box.x0, dy = PAD - box.y0;
    const ctx = out.getContext('2d');
    ctx.shadowColor = 'rgba(0, 0, 0, 0.28)';
    ctx.shadowBlur = OUTLINE;
    ctx.shadowOffsetY = OUTLINE / 3;
    for (let i = 0; i < 24; i++) {
        const a = (i / 24) * Math.PI * 2;
        ctx.drawImage(silhouette, dx + Math.cos(a) * OUTLINE, dy + Math.sin(a) * OUTLINE);
    }
    ctx.shadowColor = 'transparent';
    ctx.drawImage(cutout, dx, dy);
    return out;
}

export const sticker = {
    supported() {
        return typeof Worker === 'function' && typeof WebAssembly === 'object' && typeof OffscreenCanvas !== 'undefined';
    },

    /** A PNG sticker of the payoff panel, or null. */
    async make(placeId, panelCount) {
        try {
            if (!this.supported()) return null;
            // The full strip is a few hundred KB through this origin; the 2.5s reveal default is too tight.
            const image = await loadImage(comicImageUrl(placeId), 15000);
            if (!image) return null;
            const panel = cropPayoffPanel(image, panelCount);
            const matte = await cut(panel.data, panel.width, panel.height, await pickRuntime());
            if (!matte?.data) return null;
            const canvas = dieCut(matte);
            if (!canvas) return null;   // nothing in the panel the model was sure of
            return await new Promise((resolve) => canvas.toBlob(resolve, 'image/png'));
        } catch (err) {
            console.warn('[sticker] could not make a sticker', err);
            return null;
        }
    }
};
