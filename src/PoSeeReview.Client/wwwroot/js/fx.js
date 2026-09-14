// Single entry point for everything in js/. Loaded as a module from index.html; publishes one
// flat `window.poseeFx` surface because that is how the existing interop in this app works
// (window.geolocation, window.shareUtils) and mixing two conventions helps nobody.
//
// Every method here is defensive. Blazor calls these from component lifecycle methods, and a
// throw inside JS interop surfaces to the user as the framework's red error strip — which for
// decoration is a spectacularly bad trade. Nothing in this file may ever throw into .NET.
//
// THIS FILE IS ALSO THE COMPOSITION ROOT for the effects that pair with each other. audio.js
// does not know haptics exist, haptics.js does not know about the ambient bed, and gradient.js
// does not know about the analyser. Deciding that a tap should also buzz, or that enabling sound
// should also start a bed, is a product decision and it is made here — the same reason
// audio-reactive.js is a separate module rather than a branch inside audio.js.

import { gfx } from './gfx-core.js';
import { audio } from './audio.js';
import { haptics } from './haptics.js';
import * as ambient from './ambient.js';
import * as gradient from './gradient.js';
import * as comicFx from './comic-fx.js';
import * as comicReveal from './comic-reveal.js';
import * as particles from './particles.js';
import * as loadingRing from './loading-ring.js';
import * as viewTransitions from './view-transitions.js';
import * as audioReactive from './audio-reactive.js';
import { initPerfHud, toggle as togglePerfHud, isVisible as perfHudVisible } from './perf-hud.js';
// Only the capability probe is imported eagerly — a few lines that read navigator.gpu. The
// pipelines and WGSL that actually use it stay behind the lazy import below, so a device without
// WebGPU never fetches a byte of it.
import { isSupported as webGpuSupported } from './webgpu-pool.js';

// Three modules are NOT imported statically. Each would otherwise put code on the first-load
// path of every route that never uses it:
//
//   shelf.js         — a hand-rolled WebGL2 renderer, used on /leaderboard only.
//   physics.js       — the Verlet solver, used on the comic page at the score reveal only.
//   particles-gpu.js — the WebGPU backend, only worth fetching on a device that has WebGPU.
//
// SCRIPTS/fx-perf-check.mjs asserts shelf.js is absent on first load and present after
// /leaderboard; the same reasoning applies to the other two.
let shelfModule = null;
let physicsModule = null;
let particlesGpuModule = null;

async function loadShelf() {
    if (!shelfModule) {
        shelfModule = await import('./shelf.js');
    }
    return shelfModule;
}

async function loadPhysics() {
    if (!physicsModule) {
        physicsModule = await import('./physics.js');
    }
    return physicsModule;
}

/**
 * Resolves to the WebGPU particle backend, or null on a device without WebGPU. Probed BEFORE the
 * import so a browser that cannot use the module never fetches it.
 */
async function loadParticlesGpu() {
    if (particlesGpuModule !== null) {
        return particlesGpuModule;
    }
    try {
        const { isSupported } = await import('./webgpu-pool.js');
        if (!isSupported()) {
            particlesGpuModule = false;
            return false;
        }
        particlesGpuModule = await import('./particles-gpu.js');
        return particlesGpuModule;
    } catch {
        particlesGpuModule = false;
        return false;
    }
}

function guard(fn, fallback = null) {
    try {
        return fn();
    } catch (err) {
        console.warn('[fx] call failed', err);
        return fallback;
    }
}

async function guardAsync(fn, fallback = null) {
    try {
        return await fn();
    } catch (err) {
        console.warn('[fx] async call failed', err);
        return fallback;
    }
}

const info = gfx.init();
const audioInfo = audio.init();
haptics.init(audioInfo.enabled);
ambient.init();
viewTransitions.init();
guard(() => initPerfHud());

// Audio may already be enabled from a previous session's stored preference. The reactive driver
// has to follow that, or a returning user gets sound with a backdrop that ignores it.
guard(() => audioReactive.sync(audioInfo.enabled));

/**
 * Everything that has to move when the audio preference changes. Three modules inherit from it —
 * the visual driver, haptics and the ambient bed — and each of them would be wrong on its own:
 * a muted app with a pulsing backdrop, a muted app that still buzzes, a muted app with a drone.
 */
function propagateAudioState(enabled) {
    guard(() => audioReactive.sync(enabled));
    guard(() => haptics.syncAudio(enabled));
    guard(() => ambient.syncAudio(enabled));
    if (!enabled) {
        guard(() => haptics.cancel());
    }
}

// Reflected onto <html> so CSS can respond to the tier — this is how the glass material knows
// whether it is allowed to spend GPU time on a backdrop blur.
function reflectTier(tier) {
    guard(() => {
        document.documentElement.dataset.fxTier = tier;
    });
}
reflectTier(info.tier);
gfx.onTierChanged(reflectTier);

// Reduced motion turns audio off inside audio.js, and the three dependants have to follow it
// there too — otherwise the bed keeps playing under a muted app.
gfx.onTierChanged(() => {
    if (!audio.isEnabled()) {
        propagateAudioState(false);
    }
});

// Tracks the physics/GPU-particle handles by the canvas they were started on, so a caller only
// ever holds the one opaque integer the .NET side already models.
const backendHandles = new Map();
let nextCompositeHandle = 1;

export const fx = {
    // ── Capability + tier ────────────────────────────────────────────────────────────────
    describe: () => guard(() => ({
        ...gfx.describe(),
        webgpu: webGpuSupported(),
        haptics: haptics.describe().supported,
        narration: audio.canNarrate()
    }), {
        tier: 'off', reducedMotion: true, webgl2: false, autoDowngraded: false,
        webgpu: false, haptics: false, narration: false
    }),
    setTier: (tier) => guard(() => gfx.setTier(tier), 'off'),
    stats: () => guard(() => gfx.stats(), null),
    resetStats: () => guard(() => gfx.resetStats()),

    /** Live performance overlay. Also reachable with Ctrl+Shift+F and ?fx=debug. */
    togglePerfHud: () => guard(() => togglePerfHud(), false),
    perfHudVisible: () => guard(() => perfHudVisible(), false),

    // ── Audio ────────────────────────────────────────────────────────────────────────────
    audioEnabled: () => guard(() => audio.isEnabled(), false),

    setAudioEnabled: (enabled) => guardAsync(async () => {
        const applied = await audio.setEnabled(enabled);
        propagateAudioState(applied);
        return applied;
    }, false),

    /** Call from a real click handler or the AudioContext will not leave 'suspended'. */
    unlockAudio: () => guardAsync(async () => {
        const unlocked = await audio.unlock();
        propagateAudioState(unlocked && audio.isEnabled());
        return unlocked;
    }, false),

    audioLatency: () => guard(() => audio.latency(), null),

    /**
     * `element` is optional and pans the click to wherever the control actually is. Callers that
     * pass nothing get the old centred behaviour, so no existing call site had to change.
     */
    playTap: (element) => guard(() => {
        audio.tap(element ?? null);
        haptics.tap();
    }),
    /** Pans the click to a viewport x coordinate — see audio.tapAt. */
    playTapAt: (clientX) => guard(() => {
        audio.tapAt(clientX);
        haptics.tap();
    }),
    playScoreTick: (value, target) => guard(() => audio.scoreTick(value, target)),
    playScoreLand: (score) => guard(() => {
        audio.scoreLand(score);
        haptics.scoreLand(score);
    }),
    playPhase: (index, total) => guard(() => {
        audio.phase(index, total);
        haptics.phase();
    }),
    playSplat: (intensity) => guard(() => {
        audio.splat(intensity);
        haptics.splat(intensity);
    }),
    playShareStinger: () => guard(() => {
        audio.shareStinger();
        haptics.shareStinger();
    }),
    playError: () => guard(() => {
        audio.error();
        haptics.error();
    }),
    playConfirm: () => guard(() => {
        audio.confirm();
        haptics.confirm();
    }),
    playSeverity: (level) => guard(() => {
        audio.severity(level);
        // Remove is irreversible, so it is the one moderation action that is also felt. Hide and
        // suppress are both undoable and get sound only.
        if (level === 'remove') haptics.error();
    }),

    /**
     * The comic's own motif, seeded from its place id. Deterministic: the same restaurant always
     * plays the same figure, which is what makes it an identity rather than a flourish.
     */
    playSignature: (seed, score) => guard(() => audio.signature(seed, score)),

    /** Plays a numeric series as pitch. Used by /insights to make a chart's shape audible. */
    playSeries: (values, options) => guard(() => audio.sonify(values ?? [], options ?? {})),

    // ── Haptics ──────────────────────────────────────────────────────────────────────────
    hapticsDescribe: () => guard(() => haptics.describe(), { supported: false, enabled: false, explicit: false }),
    setHapticsEnabled: (enabled) => guard(() => haptics.setEnabled(enabled), false),

    // ── Narration ────────────────────────────────────────────────────────────────────────
    canNarrate: () => guard(() => audio.canNarrate(), false),
    narrate: (text) => guard(() => audio.narrate(text), false),
    stopNarration: () => guard(() => audio.stopNarration()),

    // ── Ambient bed ──────────────────────────────────────────────────────────────────────
    startAmbient: (score) => guardAsync(() => ambient.start(score ?? 50), false),
    setAmbientScore: (score) => guard(() => ambient.setScore(score)),
    stopAmbient: () => guard(() => ambient.stop()),
    ambientDescribe: () => guard(() => ambient.describe(), { supported: false, running: false, enabled: false, explicit: false }),
    setAmbientEnabled: (enabled) => guard(() => ambient.setEnabled(enabled), false),

    // ── Background gradient ──────────────────────────────────────────────────────────────
    startGradient: (canvas, score) => guard(() => gradient.start(canvas, { score }), 0),
    setGradientScore: (id, score) => guard(() => gradient.setScore(id, score)),
    stopGradient: (id) => guard(() => gradient.stop(id)),

    // ── Comic panel post-process ─────────────────────────────────────────────────────────
    attachComicFx: (canvas, image) => guard(() => comicFx.attach(canvas, image), 0),
    detachComicFx: (id) => guard(() => comicFx.detach(id)),

    /**
     * Fires the shockwave through the panel from the score ring. Origin is 0..1 UV; the ring
     * sits above the strip, so the caller passes the top edge rather than the centre.
     */
    comicShockwave: (id, score, originX, originY) =>
        guard(() => comicFx.shockwave(id, score, originX ?? 0.5, originY ?? 0), false),

    // ── Ink development ──────────────────────────────────────────────────────────────────

    /**
     * Develops the comic onto the page. `fxHandle` is the comic-fx handle when one attached, so
     * the shader can add its wet-ink boundary on top of the CSS mask; pass 0 and the CSS mask
     * runs alone, which is the common case.
     */
    startComicReveal: (container, bands, fxHandle) => guard(() => comicReveal.start(container, {
        bands: bands ?? 2,
        fxHandle: fxHandle ?? 0,
        // Each band gets a cue as it becomes recognisable. Composed here rather than inside
        // comic-reveal.js, which has no business knowing the app makes noise.
        onBand: (index, total) => {
            audio.panelReveal(index, total);
            haptics.tap();
        }
    }), 0),

    finishComicReveal: (id) => guard(() => comicReveal.finish(id)),

    // ── Particle burst ───────────────────────────────────────────────────────────────────

    /**
     * Fires the ink burst, preferring the WebGPU compute backend.
     *
     * The two backends are not the same effect. The WebGL2 one simulates in the vertex shader
     * from immutable seeds, so it is stateless and fades out mid-air; the compute one writes
     * state back and the ink lands. Where WebGPU exists the second is strictly better, and where
     * it does not the first is what has always shipped.
     */
    burstParticles: (canvas, score) => guardAsync(async () => {
        const gpu = await loadParticlesGpu();
        if (gpu) {
            const gpuHandle = await gpu.burst(canvas, { score });
            if (gpuHandle) {
                const handle = nextCompositeHandle++;
                backendHandles.set(handle, { kind: 'particles-gpu', id: gpuHandle });
                return handle;
            }
        }

        const glHandle = particles.burst(canvas, score);
        if (!glHandle) return 0;
        const handle = nextCompositeHandle++;
        backendHandles.set(handle, { kind: 'particles', id: glHandle });
        return handle;
    }, 0),

    stopParticles: (handle) => guardAsync(async () => {
        const entry = backendHandles.get(handle);
        if (!entry) return;
        backendHandles.delete(handle);
        if (entry.kind === 'particles-gpu') {
            const gpu = await loadParticlesGpu();
            gpu?.stop(entry.id);
        } else {
            particles.dispose(entry.id);
        }
    }),

    // ── Physics ──────────────────────────────────────────────────────────────────────────

    /**
     * Ink that lands. Runs after the burst and is where it ends up: a Verlet pile at the bottom
     * of the panel that the stateless shader sim cannot express. `mode` is 'ink' or 'shatter'.
     */
    settleInk: (canvas, score, mode, originX, originY) => guardAsync(async () => {
        const physics = await loadPhysics();
        const id = physics.start(canvas, {
            mode: mode === 'shatter' ? 'shatter' : 'ink',
            score: score ?? 50,
            originX: originX ?? 0.5,
            originY: originY ?? 0.5
        });
        if (!id) return 0;
        const handle = nextCompositeHandle++;
        backendHandles.set(handle, { kind: 'physics', id });
        return handle;
    }, 0),

    stopInk: (handle) => guardAsync(async () => {
        const entry = backendHandles.get(handle);
        if (!entry || entry.kind !== 'physics') return;
        backendHandles.delete(handle);
        const physics = await loadPhysics();
        physics.stop(entry.id);
    }),

    // ── Loading ring ─────────────────────────────────────────────────────────────────────
    startLoadingRing: (canvas, progress) => guard(() => loadingRing.start(canvas, { progress }), 0),
    setLoadingRingProgress: (id, progress) => guard(() => loadingRing.setProgress(id, progress)),
    stopLoadingRing: (id) => guard(() => loadingRing.stop(id)),

    // ── Hall of Fame shelf (lazy) ────────────────────────────────────────────────────────
    /**
     * `entries` is an array of { rank, score }. Async because the module is fetched on demand;
     * a 0 handle means it did not start, which every caller already treats as "no effect".
     * The DOM list must stay in place underneath — see the header comment in shelf.js.
     */
    startShelf: (canvas, entries) => guardAsync(async () => {
        const module = await loadShelf();
        return module.start(canvas, entries ?? []);
    }, 0),

    stopShelf: (id) => guardAsync(async () => {
        if (!id || !shelfModule) return;
        shelfModule.stop(id);
    }),

    // ── Route transitions ────────────────────────────────────────────────────────────────
    /** Called after the destination route renders, to close the open transition. */
    settleViewTransition: () => guard(() => viewTransitions.settle())

};

window.poseeFx = fx;
