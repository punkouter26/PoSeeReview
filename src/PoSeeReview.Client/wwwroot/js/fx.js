// Single entry point for everything in js/. Loaded as a module from index.html; publishes one
// flat `window.poseeFx` surface because that is how the existing interop in this app works
// (window.geolocation, window.shareUtils) and mixing two conventions helps nobody.
//
// Every method here is defensive. Blazor calls these from component lifecycle methods, and a
// throw inside JS interop surfaces to the user as the framework's red error strip — which for
// decoration is a spectacularly bad trade. Nothing in this file may ever throw into .NET.

import { gfx } from './gfx-core.js';
import { audio } from './audio.js';
import * as gradient from './gradient.js';
import * as comicFx from './comic-fx.js';
import * as particles from './particles.js';
import * as loadingRing from './loading-ring.js';
import * as viewTransitions from './view-transitions.js';
import * as audioReactive from './audio-reactive.js';
import { initPerfHud, toggle as togglePerfHud, isVisible as perfHudVisible } from './perf-hud.js';

// The 3D shelf is NOT imported statically — that would defeat the lazy loading it was written
// for, and put a renderer on the first-load path of every route that does not use it. It is
// pulled in on first use, on one page.
let shelfModule = null;

async function loadShelf() {
    if (!shelfModule) {
        shelfModule = await import('./shelf.js');
    }
    return shelfModule;
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
viewTransitions.init();
guard(() => initPerfHud());

// Audio may already be enabled from a previous session's stored preference. The reactive driver
// has to follow that, or a returning user gets sound with a backdrop that ignores it.
guard(() => audioReactive.sync(audioInfo.enabled));

// Reflected onto <html> so CSS can respond to the tier — this is how the glass material knows
// whether it is allowed to spend GPU time on a backdrop blur.
function reflectTier(tier) {
    guard(() => {
        document.documentElement.dataset.fxTier = tier;
    });
}
reflectTier(info.tier);
gfx.onTierChanged(reflectTier);

export const fx = {
    // ── Capability + tier ────────────────────────────────────────────────────────────────
    describe: () => guard(() => gfx.describe(), { tier: 'off', reducedMotion: true, webgl2: false, autoDowngraded: false }),
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
        // Kept in step here rather than inside audio.js: the analyser belongs to the audio
        // graph, but the decision to drive *visuals* from it is a composition concern, and
        // audio.js has no business knowing the gradient exists.
        audioReactive.sync(applied);
        return applied;
    }, false),

    /** Call from a real click handler or the AudioContext will not leave 'suspended'. */
    unlockAudio: () => guardAsync(async () => {
        const unlocked = await audio.unlock();
        audioReactive.sync(unlocked && audio.isEnabled());
        return unlocked;
    }, false),

    audioLatency: () => guard(() => audio.latency(), null),

    /**
     * `element` is optional and pans the click to wherever the control actually is. Callers that
     * pass nothing get the old centred behaviour, so no existing call site had to change.
     */
    playTap: (element) => guard(() => audio.tap(element ?? null)),
    /** Pans the click to a viewport x coordinate — see audio.tapAt. */
    playTapAt: (clientX) => guard(() => audio.tapAt(clientX)),
    playScoreTick: (value, target) => guard(() => audio.scoreTick(value, target)),
    playScoreLand: (score) => guard(() => audio.scoreLand(score)),
    playPhase: (index, total) => guard(() => audio.phase(index, total)),
    playSplat: (intensity) => guard(() => audio.splat(intensity)),
    playShareStinger: () => guard(() => audio.shareStinger()),
    playError: () => guard(() => audio.error()),

    // ── Background gradient ──────────────────────────────────────────────────────────────
    startGradient: (canvas, score) => guard(() => gradient.start(canvas, { score }), 0),
    setGradientScore: (id, score) => guard(() => gradient.setScore(id, score)),
    stopGradient: (id) => guard(() => gradient.stop(id)),

    // ── Comic panel post-process ─────────────────────────────────────────────────────────
    attachComicFx: (canvas, image) => guard(() => comicFx.attach(canvas, image), 0),
    detachComicFx: (id) => guard(() => comicFx.detach(id)),

    // ── Particle burst ───────────────────────────────────────────────────────────────────
    burstParticles: (canvas, score) => guard(() => particles.burst(canvas, score), 0),

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
