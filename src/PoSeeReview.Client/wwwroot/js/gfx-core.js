// Shared foundation for every graphics and audio effect in the app.
//
// Three responsibilities, all of them about keeping the frame budget honest:
//
//  1. ONE requestAnimationFrame loop. Every effect registers a callback here instead of
//     starting its own rAF. N independent loops means N wake-ups per frame and no single
//     place that can measure or stop the work.
//  2. A frame-time budget with AUTOMATIC DOWNGRADE. If the rolling frame cost stays over
//     budget, the tier drops itself. "Sustains 60 FPS" is not something you can assert by
//     writing careful shaders — it has to be measured on the device that is actually running.
//  3. An effects TIER, so a cheap phone, a battery-saver user, and someone who asked their OS
//     for reduced motion all get something coherent rather than whatever happens to be cheap.
//
// Tiers:
//   'off'  — no GPU loops, no audio, no physics. Static CSS only.
//   'lite' — audio + CSS materials. No persistent GPU loop.
//   'full' — everything, including the heavy lazy-loaded scenes.

import { acquireSurface, poolStats } from './gl-pool.js';
import {
    startTelemetry, telemetrySnapshot, resetTelemetry,
    attachGpuTimer, beginGpuSample, endGpuSample
} from './telemetry.js';

const STORAGE_KEY = 'posee_fx_tier';
const TIERS = ['off', 'lite', 'full'];

// 60 FPS is 16.67ms. Budget the frame at 20ms so an occasional GC pause is not treated as a
// regression, but a genuinely overloaded device still trips the downgrade.
const FRAME_BUDGET_MS = 20;

// Measured in MILLISECONDS of sustained overrun, not frames. A frame count cannot express
// "1.5 seconds": 90 frames is 1.5s only at 60 FPS, and by the time this guard matters the
// frames are slow — at 60ms each, 90 frames is 5.4s. The guard was therefore slowest to fire
// exactly when the device most needed it. Timing the streak makes the delay constant.
const OVER_BUDGET_MS_BEFORE_DOWNGRADE = 1500;

const state = {
    tier: 'off',
    tierWasAutoDowngraded: false,
    reducedMotion: false,
    webgl2: false,
    tasks: new Map(),
    nextTaskId: 1,
    rafHandle: 0,
    lastFrameStart: 0,
    overBudgetStreak: 0,
    stats: {
        fps: 0,
        frameMs: 0,
        worstFrameMs: 0,
        droppedFrames: 0,
        sampledFrames: 0,
        activeTasks: 0,
        // Time this loop spent in effect callbacks, as opposed to wall time between frames.
        // The gap between the two is everything else on the main thread — Blazor renders, GC,
        // layout — which is exactly what you need to know before optimising a shader that was
        // never the problem.
        cpuMs: 0
    },
    // Rolling mean, cheap: no array allocation per frame.
    frameMsAccumulator: 0,
    frameMsCount: 0,
    cpuMsAccumulator: 0,
    // A short ring of recent frame times, for the sparkline in the live HUD. Fixed length and
    // preallocated: an overlay that allocates per frame is a source of the jank it reports.
    history: new Float32Array(120),
    historyIndex: 0,
    lastStatsFlush: 0,
    listeners: new Set()
};

function detectReducedMotion() {
    try {
        return window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    } catch {
        return false;
    }
}

function detectWebGl2() {
    try {
        const canvas = document.createElement('canvas');
        return !!canvas.getContext('webgl2');
    } catch {
        return false;
    }
}

/**
 * The tier a device should get before the user has expressed any preference. Deliberately
 * conservative: a first visit that stutters is a worse introduction than one that is plain.
 */
function detectDefaultTier() {
    if (state.reducedMotion || !state.webgl2) {
        return 'off';
    }

    try {
        // Save-Data is an explicit request not to spend the user's bytes; the heavy tier
        // lazy-loads megabytes of library code, so it is exactly what they are asking to avoid.
        if (navigator.connection?.saveData) {
            return 'lite';
        }
        // deviceMemory is Chromium-only and coarse, but a 2GB phone genuinely cannot hold
        // Three.js, Rapier and a WASM runtime at once without swapping.
        if (typeof navigator.deviceMemory === 'number' && navigator.deviceMemory <= 4) {
            return 'lite';
        }
        if (typeof navigator.hardwareConcurrency === 'number' && navigator.hardwareConcurrency <= 4) {
            return 'lite';
        }
    } catch {
        // Feature detection failing is not a reason to refuse a tier.
    }

    return 'full';
}

function readStoredTier() {
    try {
        const stored = localStorage.getItem(STORAGE_KEY);
        return TIERS.includes(stored) ? stored : null;
    } catch {
        return null; // Private mode / blocked storage.
    }
}

function notifyTierChanged() {
    for (const listener of state.listeners) {
        try {
            listener(state.tier);
        } catch {
            // A misbehaving effect must not stop the others from being told.
        }
    }
}

function frame(now) {
    state.rafHandle = 0;

    const elapsed = state.lastFrameStart ? now - state.lastFrameStart : 0;
    state.lastFrameStart = now;

    const workStart = performance.now();

    // The GPU sample brackets every effect in the frame, not one of them. A per-effect query
    // would need one query object per effect per frame and would serialise them against each
    // other; what the overlay actually needs to answer is "is the GPU or the CPU the wall?".
    beginGpuSample();

    for (const task of state.tasks.values()) {
        try {
            task.callback(now, elapsed);
        } catch (err) {
            // One broken effect must not take the whole loop down with it.
            console.error(`[gfx] task "${task.name}" threw; unregistering`, err);
            state.tasks.delete(task.id);
        }
    }

    endGpuSample();

    const workMs = performance.now() - workStart;
    recordFrame(elapsed, workMs, now);

    if (state.tasks.size > 0) {
        state.rafHandle = requestAnimationFrame(frame);
    } else {
        state.lastFrameStart = 0;
    }
}

function recordFrame(elapsedMs, workMs, now) {
    const stats = state.stats;
    stats.activeTasks = state.tasks.size;

    if (elapsedMs <= 0) {
        return;
    }

    stats.sampledFrames++;
    state.frameMsAccumulator += elapsedMs;
    state.cpuMsAccumulator += workMs;
    state.frameMsCount++;

    state.history[state.historyIndex] = elapsedMs;
    state.historyIndex = (state.historyIndex + 1) % state.history.length;

    if (elapsedMs > stats.worstFrameMs) {
        stats.worstFrameMs = elapsedMs;
    }

    if (elapsedMs > FRAME_BUDGET_MS) {
        stats.droppedFrames++;
        state.overBudgetStreak += elapsedMs;

        if (state.overBudgetStreak >= OVER_BUDGET_MS_BEFORE_DOWNGRADE) {
            autoDowngrade();
        }
    } else {
        state.overBudgetStreak = 0;
    }

    // Flush the rolling average about four times a second — often enough for a live readout,
    // rare enough that the diagnostics overlay is not itself a source of jank.
    if (now - state.lastStatsFlush >= 250) {
        stats.frameMs = state.frameMsAccumulator / Math.max(1, state.frameMsCount);
        stats.cpuMs = state.cpuMsAccumulator / Math.max(1, state.frameMsCount);
        stats.fps = stats.frameMs > 0 ? 1000 / stats.frameMs : 0;
        state.frameMsAccumulator = 0;
        state.cpuMsAccumulator = 0;
        state.frameMsCount = 0;
        state.lastStatsFlush = now;
    }
}

/**
 * Steps the tier down one level after sustained overrun. This is the mechanism that turns
 * "should hit 60 FPS" into "does, or stops trying" — without it, a mid-range phone just
 * renders everything badly forever.
 */
function autoDowngrade() {
    const index = TIERS.indexOf(state.tier);
    if (index <= 0) {
        state.overBudgetStreak = 0;
        return;
    }

    const next = TIERS[index - 1];
    console.warn(`[gfx] sustained frame overrun; dropping effects tier ${state.tier} -> ${next}`);
    state.tier = next;
    state.tierWasAutoDowngraded = true;
    state.overBudgetStreak = 0;
    state.stats.worstFrameMs = 0;

    // Deliberately NOT persisted. A downgrade caused by one heavy page or a background tab
    // stealing the GPU should not silently become the user's permanent setting.
    notifyTierChanged();
}

function ensureLoopRunning() {
    if (!state.rafHandle && state.tasks.size > 0 && !document.hidden) {
        state.lastFrameStart = 0;
        state.rafHandle = requestAnimationFrame(frame);
    }
}

function stopLoop() {
    if (state.rafHandle) {
        cancelAnimationFrame(state.rafHandle);
        state.rafHandle = 0;
    }
    state.lastFrameStart = 0;
}

// A hidden tab keeps its rAF callbacks queued in some browsers and throttled in others; either
// way, animating a page nobody is looking at is pure battery cost.
document.addEventListener('visibilitychange', () => {
    if (document.hidden) {
        stopLoop();
    } else {
        ensureLoopRunning();
    }
});

export const gfx = {
    TIERS,

    init() {
        state.reducedMotion = detectReducedMotion();
        state.webgl2 = detectWebGl2();
        startTelemetry();

        const stored = readStoredTier();
        // A stored preference is still overridden by an OS-level reduced-motion request and by
        // a device with no WebGL2 — neither is a preference we are entitled to ignore.
        state.tier = (state.reducedMotion || !state.webgl2)
            ? 'off'
            : (stored ?? detectDefaultTier());

        return this.describe();
    },

    describe() {
        return {
            tier: state.tier,
            reducedMotion: state.reducedMotion,
            webgl2: state.webgl2,
            autoDowngraded: state.tierWasAutoDowngraded
        };
    },

    tier: () => state.tier,

    /** True when the requested level is at or below what this device is currently running. */
    allows(level) {
        return TIERS.indexOf(state.tier) >= TIERS.indexOf(level);
    },

    setTier(tier) {
        if (!TIERS.includes(tier)) {
            return state.tier;
        }

        if (state.reducedMotion && tier !== 'off') {
            // Honour the OS. Offering the control and then ignoring it is worse than hiding it.
            return state.tier;
        }

        state.tier = tier;
        state.tierWasAutoDowngraded = false;
        try {
            localStorage.setItem(STORAGE_KEY, tier);
        } catch {
            // Preference simply will not persist; the session still respects it.
        }
        notifyTierChanged();
        return state.tier;
    },

    onTierChanged(listener) {
        state.listeners.add(listener);
        return () => state.listeners.delete(listener);
    },

    /** Registers a per-frame callback. Returns an unregister function. */
    addTask(name, callback) {
        const id = state.nextTaskId++;
        state.tasks.set(id, { id, name, callback });
        ensureLoopRunning();
        return () => {
            state.tasks.delete(id);
            if (state.tasks.size === 0) {
                stopLoop();
            }
        };
    },

    stats() {
        const pool = poolStats();
        return {
            ...state.stats,
            ...telemetrySnapshot(),
            tier: state.tier,
            autoDowngraded: state.tierWasAutoDowngraded,
            glContexts: pool.pooledContexts + pool.directContexts,
            pooledContexts: pool.pooledContexts,
            directContexts: pool.directContexts,
            glSurfaces: pool.surfaces,
            contextLosses: pool.contextLosses
        };
    },

    /** Raw frame-time ring for the sparkline, oldest first. Copied, so callers cannot corrupt it. */
    frameHistory() {
        const out = new Array(state.history.length);
        for (let i = 0; i < state.history.length; i++) {
            out[i] = state.history[(state.historyIndex + i) % state.history.length];
        }
        return out;
    },

    resetStats() {
        Object.assign(state.stats, {
            fps: 0, frameMs: 0, worstFrameMs: 0, droppedFrames: 0, sampledFrames: 0,
            cpuMs: 0, activeTasks: state.tasks.size
        });
        state.frameMsAccumulator = 0;
        state.cpuMsAccumulator = 0;
        state.frameMsCount = 0;
        state.overBudgetStreak = 0;
        state.history.fill(0);
        state.historyIndex = 0;
        resetTelemetry();
    }
};

// ── Minimal WebGL2 helpers ───────────────────────────────────────────────────────────────
//
// Hand-rolled rather than pulled from a library: the effects here draw a fullscreen triangle
// and one instanced quad batch. That is a few dozen lines, against ~130KB gzipped for a
// renderer whose feature set this app would not touch.

/**
 * Preferred way to get a render target. Hands back a pooled surface where the browser supports
 * one, and a privately-owned context where it does not — the caller's code is identical either
 * way. Also the point where the GPU timer is bound, since that has to happen on whatever context
 * the effects actually ended up sharing.
 *
 * Usage per frame: `if (!surface.beginFrame()) return;` … draw … `surface.present();`
 * On teardown: `surface.release()`.
 */
export function createSurface(canvas, options = {}) {
    const surface = acquireSurface(canvas, options);
    if (surface) {
        attachGpuTimer(surface.gl);
    }
    return surface;
}

/** @deprecated Use createSurface. Retained for effects that need the default framebuffer. */
export function createGl(canvas) {
    return canvas.getContext('webgl2', {
        alpha: true,
        antialias: false,      // These are post-process passes; MSAA buys nothing and costs fill rate.
        depth: false,
        stencil: false,
        premultipliedAlpha: true,
        powerPreference: 'low-power',
        desynchronized: true
    });
}

export function compileProgram(gl, vertexSource, fragmentSource) {
    const compile = (type, source) => {
        const shader = gl.createShader(type);
        gl.shaderSource(shader, source);
        gl.compileShader(shader);
        if (!gl.getShaderParameter(shader, gl.COMPILE_STATUS)) {
            const log = gl.getShaderInfoLog(shader);
            gl.deleteShader(shader);
            throw new Error(`shader compile failed: ${log}`);
        }
        return shader;
    };

    const vs = compile(gl.VERTEX_SHADER, vertexSource);
    const fs = compile(gl.FRAGMENT_SHADER, fragmentSource);
    const program = gl.createProgram();
    gl.attachShader(program, vs);
    gl.attachShader(program, fs);
    gl.linkProgram(program);

    // Shader objects are reference-counted by the program; detaching lets the driver free the
    // source immediately instead of holding it for the page's lifetime.
    gl.detachShader(program, vs);
    gl.detachShader(program, fs);
    gl.deleteShader(vs);
    gl.deleteShader(fs);

    if (!gl.getProgramParameter(program, gl.LINK_STATUS)) {
        const log = gl.getProgramInfoLog(program);
        gl.deleteProgram(program);
        throw new Error(`program link failed: ${log}`);
    }

    return program;
}

/**
 * Vertex shader for a single triangle that covers the viewport. Preferred over two triangles:
 * no diagonal seam, and the GPU rasterises one primitive instead of two.
 */
export const FULLSCREEN_VERTEX_SHADER = `#version 300 es
out vec2 vUv;
void main() {
    vec2 pos = vec2((gl_VertexID << 1) & 2, gl_VertexID & 2);
    vUv = pos;
    gl_Position = vec4(pos * 2.0 - 1.0, 0.0, 1.0);
}`;

/**
 * Sizes the drawing buffer to the element, capping device pixel ratio. A phone reporting DPR 3
 * would otherwise ask a mobile GPU for nine times the fill rate of the CSS pixel count, which
 * is the single most common way a "cheap" fullscreen shader stops being cheap.
 */
export function resizeToDisplay(canvas, gl, maxDpr = 2) {
    const dpr = Math.min(window.devicePixelRatio || 1, maxDpr);
    const width = Math.max(1, Math.floor(canvas.clientWidth * dpr));
    const height = Math.max(1, Math.floor(canvas.clientHeight * dpr));

    if (canvas.width !== width || canvas.height !== height) {
        canvas.width = width;
        canvas.height = height;
        gl.viewport(0, 0, width, height);
        return true;
    }
    return false;
}

/** Frees GPU resources deterministically instead of waiting for the context to be collected. */
export function disposeGl(gl) {
    if (!gl) return;
    try {
        gl.getExtension('WEBGL_lose_context')?.loseContext();
    } catch {
        // Extension is optional; the context will be reclaimed with the canvas.
    }
}

window.poseeGfx = gfx;
