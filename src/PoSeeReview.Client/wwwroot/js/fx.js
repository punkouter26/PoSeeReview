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

// The two heavy modules are NOT imported statically — that would defeat the lazy loading they
// were written for. They are pulled in on first use.

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
audio.init();
viewTransitions.init();

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

    // ── Audio ────────────────────────────────────────────────────────────────────────────
    audioEnabled: () => guard(() => audio.isEnabled(), false),
    setAudioEnabled: (enabled) => guardAsync(() => audio.setEnabled(enabled), false),
    /** Call from a real click handler or the AudioContext will not leave 'suspended'. */
    unlockAudio: () => guardAsync(() => audio.unlock(), false),

    playTap: () => guard(() => audio.tap()),
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

    // ── Route transitions ────────────────────────────────────────────────────────────────
    /** Called after the destination route renders, to close the open transition. */
    settleViewTransition: () => guard(() => viewTransitions.settle())

};

window.poseeFx = fx;
