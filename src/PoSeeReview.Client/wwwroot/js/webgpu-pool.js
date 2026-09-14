// One shared GPUDevice for the whole app, on the same terms gl-pool.js shares one WebGL2 context.
//
// WHY THIS EXISTS ALONGSIDE gl-pool RATHER THAN REPLACING IT.
//
// WebGPU is not a faster WebGL. The thing it has that WebGL2 does not is COMPUTE, and compute is
// what the particle burst actually wants: the WebGL2 version simulates every particle in the
// vertex shader from immutable seeds, which is why 1500 of them cost the same as 20 — and also
// why no particle can ever know about the floor or about another particle. A compute pass writes
// state back to a storage buffer, so the ink can decelerate, hit the bottom of the panel and
// stay there. That is a different effect, not a faster one.
//
// Everything else in the app is a fullscreen fragment pass, where WebGL2 is entirely adequate
// and already pooled. Porting those would mean maintaining two shader languages for identical
// output. So this file is deliberately NARROW: it owns the device and the canvas configuration,
// and exactly one effect is built on it, with the WebGL2 version kept as the fallback.
//
// THE POOLING RULE IS THE SAME. A GPUDevice is expensive to create and browsers cap how many a
// page may hold; requesting one per effect would repeat precisely the mistake gl-pool was written
// to fix. One device, requested once, shared by everything, and never re-requested after a
// failure — a device that could not be created will not appear later in the same page.
//
// FAILS CLOSED, ALWAYS. No navigator.gpu, no adapter, a device that reports lost: every path
// returns null and the caller runs its WebGL2 version. WebGPU here is an upgrade, never a
// requirement, and nothing in the app is unavailable without it.

const state = {
    supported: null,      // null = not yet probed
    device: null,
    format: null,
    // The in-flight request, so N simultaneous callers share one adapter request rather than
    // racing to create N devices.
    pending: null,
    failed: false,
    lost: false,
    canvasTargets: 0,
    lostCount: 0
};

export function isSupported() {
    if (state.supported !== null) return state.supported;
    try {
        state.supported = typeof navigator !== 'undefined'
            && !!navigator.gpu
            && typeof navigator.gpu.requestAdapter === 'function';
    } catch {
        state.supported = false;
    }
    return state.supported;
}

/**
 * The shared device. Resolves to `{ device, format }` or null.
 *
 * `powerPreference: 'low-power'` matches the WebGL2 attributes: every effect in this app is
 * decoration over a working page, and none of it is worth waking a discrete GPU for.
 */
export async function acquireDevice() {
    if (state.failed || !isSupported()) return null;
    if (state.device && !state.lost) {
        return { device: state.device, format: state.format };
    }
    if (state.pending) return state.pending;

    state.pending = (async () => {
        try {
            const adapter = await navigator.gpu.requestAdapter({ powerPreference: 'low-power' });
            if (!adapter) {
                state.failed = true;
                return null;
            }

            const device = await adapter.requestDevice();
            if (!device) {
                state.failed = true;
                return null;
            }

            state.device = device;
            state.lost = false;
            state.format = navigator.gpu.getPreferredCanvasFormat();

            // A lost device invalidates every buffer, pipeline and bind group made from it. The
            // flag is what lets the next acquire re-request instead of handing out a corpse; the
            // effects notice through their own `device.lost` handling and tear down.
            device.lost.then((info) => {
                state.lost = true;
                state.lostCount++;
                // 'destroyed' is us calling destroy(); anything else is the driver or the browser.
                if (info?.reason !== 'destroyed') {
                    console.warn('[webgpu] device lost; effects fall back to WebGL2', info?.message);
                }
            }).catch(() => { /* the promise itself failing tells us nothing actionable */ });

            // Uncaptured validation errors are otherwise silent, and a silently wrong pipeline
            // looks exactly like a slow one.
            try {
                device.addEventListener('uncapturederror', (event) => {
                    console.warn('[webgpu] uncaptured error', event.error?.message ?? event.error);
                });
            } catch { /* not an EventTarget in some early implementations */ }

            return { device, format: state.format };
        } catch (err) {
            console.warn('[webgpu] device unavailable; staying on WebGL2', err);
            state.failed = true;
            return null;
        } finally {
            state.pending = null;
        }
    })();

    return state.pending;
}

/**
 * Configures a canvas to present from the shared device.
 *
 * `alphaMode: 'premultiplied'` is required, not stylistic: these canvases sit over the comic and
 * over the DOM, so an opaque backing would paint a black rectangle across the thing the effect is
 * decorating.
 *
 * @returns {{ context: GPUCanvasContext, format: GPUTextureFormat, resize: () => {width:number,height:number} } | null}
 */
export function configureCanvas(canvas, device, format, maxDpr = 2) {
    if (!canvas || !device) return null;

    let context;
    try {
        context = canvas.getContext('webgpu');
    } catch {
        return null;
    }
    if (!context) return null;

    try {
        context.configure({ device, format, alphaMode: 'premultiplied' });
    } catch (err) {
        console.warn('[webgpu] canvas configure failed', err);
        return null;
    }

    state.canvasTargets++;

    const resize = () => {
        const dpr = Math.min(window.devicePixelRatio || 1, maxDpr);
        const width = Math.max(1, Math.floor((canvas.clientWidth || 1) * dpr));
        const height = Math.max(1, Math.floor((canvas.clientHeight || 1) * dpr));
        if (canvas.width !== width || canvas.height !== height) {
            canvas.width = width;
            canvas.height = height;
            // A WebGPU canvas does NOT need reconfiguring on resize — the swap chain follows the
            // backing store — but the caller usually needs to know the size changed.
            return { width, height, changed: true };
        }
        return { width, height, changed: false };
    };

    return {
        context,
        format,
        resize,
        release() {
            try { context.unconfigure(); } catch { /* already gone */ }
            state.canvasTargets = Math.max(0, state.canvasTargets - 1);
        }
    };
}

/** Merged into gfx.stats() so the diagnostics overlay can say which backend actually ran. */
export function gpuStats() {
    return {
        webgpuSupported: isSupported(),
        webgpuActive: !!state.device && !state.lost,
        webgpuCanvases: state.canvasTargets,
        webgpuDeviceLosses: state.lostCount
    };
}

window.poseeWebGpu = { isSupported, gpuStats };
