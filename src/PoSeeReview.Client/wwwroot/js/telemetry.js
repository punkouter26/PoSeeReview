// Performance telemetry beyond frame time.
//
// The frame-budget watchdog in gfx-core answers "are we over 20ms?" but not "why". Those are
// very different questions and only the second one is actionable:
//
//   * A slow frame caused by a fragment shader and a slow frame caused by a 200ms Blazor render
//     look identical in a frame-time average, and have opposite fixes. GPU timer queries
//     separate them.
//   * A user's complaint is almost never about FPS. It is about the tap that took 400ms to do
//     anything, which is INP — measured from the interaction, not from the render loop.
//   * A WebGL context count that creeps upward across navigations is the signature of a leak
//     that ends in silent context eviction. It is invisible in every frame-time metric.
//
// Every source here is optional and independently guarded. Chromium has all of them, Firefox and
// Safari have some, and a missing one reports null rather than zero — an absent measurement and
// a measurement of zero are not the same claim.

const LONG_TASK_MS = 50;   // The spec's definition; also roughly where users notice.

const state = {
    longTasks: 0,
    longTaskMsTotal: 0,
    worstLongTaskMs: 0,
    inpMs: null,
    worstInteractionMs: 0,
    interactions: 0,
    layoutShift: 0,
    observers: [],
    started: false,

    gpu: {
        supported: false,
        ext: null,
        gl: null,
        query: null,
        // Two distinct states, and conflating them is a bug: `active` means beginQuery has been
        // issued and endQuery has NOT, `pending` means the query is closed and the GPU has not
        // returned its result yet. A single flag makes the loop call endQuery twice on any frame
        // whose result was not ready, which WebGL rejects with INVALID_OPERATION every frame.
        active: false,
        pending: false,
        lastMs: null,
        emaMs: null
    }
};

// ── Long tasks, interactions, layout shift ───────────────────────────────────────────────

function observe(type, handler, extra = {}) {
    try {
        const observer = new PerformanceObserver((list) => {
            for (const entry of list.getEntries()) {
                try { handler(entry); } catch { /* one bad entry must not kill the observer */ }
            }
        });
        observer.observe({ type, buffered: true, ...extra });
        state.observers.push(observer);
        return true;
    } catch {
        // Unsupported entry type. Firefox has no 'event', Safari had no 'longtask' for years.
        return false;
    }
}

export function startTelemetry() {
    if (state.started) return;
    state.started = true;

    observe('longtask', (entry) => {
        state.longTasks++;
        state.longTaskMsTotal += entry.duration;
        if (entry.duration > state.worstLongTaskMs) {
            state.worstLongTaskMs = entry.duration;
        }
    });

    // INP proper needs the 98th percentile over the session. That is more bookkeeping than a
    // live overlay warrants, so this reports the WORST interaction instead — a strictly
    // pessimistic stand-in, which is the right direction to be wrong in for a diagnostic.
    observe('event', (entry) => {
        const latency = entry.processingEnd - entry.startTime;
        if (!Number.isFinite(latency)) return;
        state.interactions++;
        if (latency > state.worstInteractionMs) {
            state.worstInteractionMs = latency;
            state.inpMs = latency;
        }
    }, { durationThreshold: 16 });

    // CLS is in scope here for one specific reason: the boot splash regression documented in
    // CLAUDE.md measured 0.20 on mobile, and login/logout both navigate with forceLoad. A live
    // number makes a reintroduction visible immediately instead of at the next audit.
    observe('layout-shift', (entry) => {
        if (!entry.hadRecentInput) {
            state.layoutShift += entry.value;
        }
    });
}

// ── GPU timing ───────────────────────────────────────────────────────────────────────────

/**
 * Attaches a GPU timer to the pool's context. One query in flight at a time: the result is not
 * available until the GPU drains, and polling a second query while the first is pending is how
 * you end up stalling the pipeline you were trying to measure.
 */
export function attachGpuTimer(gl) {
    if (!gl || state.gpu.gl === gl) return state.gpu.supported;

    try {
        const ext = gl.getExtension('EXT_disjoint_timer_query_webgl2');
        if (!ext) {
            state.gpu.supported = false;
            return false;
        }
        state.gpu.ext = ext;
        state.gpu.gl = gl;
        state.gpu.supported = true;
    } catch {
        state.gpu.supported = false;
    }
    return state.gpu.supported;
}

export function beginGpuSample() {
    const gpu = state.gpu;
    // One query in flight at a time. Starting a second while the first is still resolving would
    // either stall the pipeline being measured or leak query objects every frame.
    if (!gpu.supported || gpu.active || gpu.pending) return;

    const { gl, ext } = gpu;
    try {
        gpu.query = gl.createQuery();
        gl.beginQuery(ext.TIME_ELAPSED_EXT, gpu.query);
        gpu.active = true;
    } catch {
        gpu.active = false;
        gpu.pending = false;
        if (gpu.query) {
            try { gl.deleteQuery(gpu.query); } catch { /* context gone */ }
        }
        gpu.query = null;
    }
}

export function endGpuSample() {
    const gpu = state.gpu;
    if (!gpu.supported || !gpu.query) return;

    const { gl, ext } = gpu;

    // Close the query, but ONLY if this frame actually opened one.
    if (gpu.active) {
        try {
            gl.endQuery(ext.TIME_ELAPSED_EXT);
        } catch {
            // Nothing further can be read from it.
            try { gl.deleteQuery(gpu.query); } catch { /* context gone */ }
            gpu.query = null;
            gpu.active = false;
            gpu.pending = false;
            return;
        }
        gpu.active = false;
        gpu.pending = true;
    }

    if (!gpu.pending) return;

    // Poll without blocking. Waiting on the result would synchronise the CPU to the GPU and
    // destroy the very frame time being measured, so an unresolved query simply stays pending
    // and is read on a later frame.
    try {
        const available = gl.getQueryParameter(gpu.query, gl.QUERY_RESULT_AVAILABLE);
        const disjoint = gl.getParameter(ext.GPU_DISJOINT_EXT);

        if (available && !disjoint) {
            const ms = gl.getQueryParameter(gpu.query, gl.QUERY_RESULT) / 1e6;
            gpu.lastMs = ms;
            // Smoothed: raw GPU timings are noisy enough to be unreadable in a live overlay.
            gpu.emaMs = gpu.emaMs === null ? ms : gpu.emaMs * 0.9 + ms * 0.1;
        }

        if (available || disjoint) {
            gl.deleteQuery(gpu.query);
            gpu.query = null;
            gpu.pending = false;
        }
    } catch {
        try { gl.deleteQuery(gpu.query); } catch { /* context gone */ }
        gpu.query = null;
        gpu.pending = false;
    }
}

// ── Snapshot ─────────────────────────────────────────────────────────────────────────────

function heapMb() {
    try {
        // Chromium only, and coarsened for cross-origin isolation reasons. Still the only
        // in-page signal that separates "the app is slow" from "the app is leaking".
        const memory = performance.memory;
        if (!memory || typeof memory.usedJSHeapSize !== 'number') return null;
        return memory.usedJSHeapSize / (1024 * 1024);
    } catch {
        return null;
    }
}

export function telemetrySnapshot() {
    return {
        gpuMs: state.gpu.emaMs,
        gpuSupported: state.gpu.supported,
        heapMb: heapMb(),
        longTasks: state.longTasks,
        longTaskMsTotal: state.longTaskMsTotal,
        worstLongTaskMs: state.worstLongTaskMs,
        inpMs: state.inpMs,
        interactions: state.interactions,
        layoutShift: state.layoutShift
    };
}

export function resetTelemetry() {
    state.longTasks = 0;
    state.longTaskMsTotal = 0;
    state.worstLongTaskMs = 0;
    state.inpMs = null;
    state.worstInteractionMs = 0;
    state.interactions = 0;
    state.layoutShift = 0;
    state.gpu.lastMs = null;
    state.gpu.emaMs = null;
}

export function stopTelemetry() {
    for (const observer of state.observers) {
        try { observer.disconnect(); } catch { /* already gone */ }
    }
    state.observers = [];
    state.started = false;
}
