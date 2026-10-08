// Single entry point for everything in js/. Loaded as a module from index.html; publishes one
// flat `window.poseeFx` surface because that is how the existing interop in this app works
// (window.geolocation, window.shareUtils) and mixing two conventions helps nobody.
//
// Every method here is defensive. Blazor calls these from component lifecycle methods, and a
// throw inside JS interop surfaces to the user as the framework's red error strip — which for
// decoration is a spectacularly bad trade. Nothing in this file may ever throw into .NET.
//
// THIS FILE IS ALSO THE COMPOSITION ROOT for the effects that pair with each other. audio.js
// does not know haptics exist and comic-reveal.js does not know the app makes noise. Deciding
// that a tap should also buzz is a product decision and it is made here.

import { gfx } from './gfx-core.js';
import { audio } from './audio.js';
import { haptics } from './haptics.js';
import * as gradient from './gradient.js';
import * as panelScrub from './panel-scrub.js';
import * as comicReveal from './comic-reveal.js';
import * as viewTransitions from './view-transitions.js';

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

viewTransitions.init({
    onNavigate: (direction, clientX) => guard(() => audio.navigate(direction, clientX))
});

function propagateAudioState(enabled) {
    guard(() => haptics.syncAudio(enabled));
    if (!enabled) {
        guard(() => haptics.cancel());
    }
}

function reflectTier(tier) {
    guard(() => {
        document.documentElement.dataset.fxTier = tier;
    });
}
reflectTier(info.tier);
gfx.onTierChanged(reflectTier);

/** Heard and felt when sound or haptics is switched ON: proof the switch did something. */
function toggleTick() {
    audio.tapAt(window.innerWidth / 2);
    haptics.tap();
}

// At most this many rows of a reshuffled board make a sound; the rest only move.
const MAX_VOICED_MOVES = 4;

export const fx = {
    // ── Capability + tier ────────────────────────────────────────────────────────────────
    describe: () => guard(() => ({
        ...gfx.describe(),
        haptics: haptics.describe().supported,
        narration: audio.canNarrate()
    }), {
        tier: 'off', reducedMotion: true, webgl2: false, autoDowngraded: false,
        haptics: false, narration: false
    }),
    setTier: (tier) => guard(() => gfx.setTier(tier), 'off'),
    stats: () => guard(() => gfx.stats(), null),
    pageVisible: () => guard(() => document.visibilityState === 'visible', false),

    // ── Audio ────────────────────────────────────────────────────────────────────────────
    audioEnabled: () => guard(() => audio.isEnabled(), false),

    setAudioEnabled: (enabled) => guardAsync(async () => {
        const applied = await audio.setEnabled(enabled);
        propagateAudioState(applied);
        if (applied) toggleTick();
        return applied;
    }, false),

    volume: () => guard(() => audio.volume(), 1),
    setVolume: (volume) => guard(() => audio.setVolume(volume), 1),

    /** Call from a real click handler or the AudioContext will not leave 'suspended'. */
    unlockAudio: () => guardAsync(async () => {
        const unlocked = await audio.unlock();
        propagateAudioState(unlocked && audio.isEnabled());
        return unlocked;
    }, false),

    /** Pans the click to a viewport x coordinate — see audio.tapAt. */
    playTapAt: (clientX) => guard(() => {
        audio.tapAt(clientX);
        haptics.tap();
    }),
    /**
     * The discovery beats. These exist because the landing page — the first screen every visitor
     * sees — had exactly two cues on it: a tap, and the audio unlock hung off the same handler.
     * Asking for your location, waiting, and getting fifteen restaurants back all sounded
     * identical to pressing a button.
     */
    playLocating: () => guard(() => {
        audio.locating();
        haptics.tap();
    }),
    playArrival: (count) => guard(() => {
        audio.arrival(count ?? 0);
        haptics.confirm();
    }),
    /** Zero results. Not an error cue: an empty answer is an answer. */
    playEmpty: () => guard(() => audio.empty()),

    /**
     * Tap on a card, voiced by whether the comic already exists.
     *
     * A cache hit opens instantly and costs nothing; a miss spends a paid image call and about
     * ten seconds. Same control, wildly different consequence, and until now the same click.
     */
    playTapCached: (clientX, cached) => guard(() => {
        if (cached) {
            audio.tapCached(clientX);
            haptics.tap();
        } else {
            audio.tapUncached(clientX);
            haptics.tap();
        }
    }),

    playScoreTick: (value, target) => guard(() => audio.scoreTick(value, target)),
    playScoreLand: (score) => guard(() => {
        audio.scoreLand(score);
        haptics.confirm();
    }),
    /** The whole count-up and its landing chord in one call, timed by the audio clock. */
    playScoreRoll: (score, durationMs) => guard(() => {
        audio.scoreRoll(score, durationMs);
        setTimeout(() => guard(() => haptics.confirm()), Math.max(0, durationMs ?? 0));
    }),
    playShareStinger: () => guard(() => {
        audio.shareStinger();
        haptics.confirm();
    }),
    playError: () => guard(() => {
        audio.error();
        haptics.error();
    }),
    playConfirm: () => guard(() => {
        audio.confirm();
        haptics.confirm();
    }),
    /**
     * The comic's own motif, seeded from its place id. Deterministic: the same restaurant always
     * plays the same figure, which is what makes it an identity rather than a flourish.
     */
    playSignature: (seed, score) => guard(() => audio.signature(seed, score)),

    // ── Haptics ──────────────────────────────────────────────────────────────────────────
    hapticsDescribe: () => guard(() => haptics.describe(), { supported: false, enabled: false, explicit: false }),
    setHapticsEnabled: (enabled) => guard(() => {
        const active = haptics.setEnabled(enabled);
        if (active) toggleTick();
        return active;
    }, false),

    // ── Narration ────────────────────────────────────────────────────────────────────────
    canNarrate: () => guard(() => audio.canNarrate(), false),
    narrate: (text) => guard(() => audio.narrate(text), false),
    stopNarration: () => guard(() => audio.stopNarration()),
    /** Resolves when the current utterance is over, or at once if nothing is speaking. */
    narrationEnded: () => guardAsync(() => audio.narrationEnded()),

    // ── Background gradient ──────────────────────────────────────────────────────────────
    // The backdrop's audio meter is handed over here: gradient.js does not import audio.js.
    startGradient: (canvas, score) => guard(() => gradient.start(canvas, { score, level: audio.level }), 0),
    setGradientScore: (id, score) => guard(() => gradient.setScore(id, score)),
    stopGradient: (id) => guard(() => gradient.stop(id)),

    // ── Ink development ──────────────────────────────────────────────────────────────────

    /** Develops the comic onto the page with a CSS mask. */
    startComicReveal: (container, bands) => guard(() => comicReveal.start(container, {
        bands: bands ?? 2,
        // Each band gets a cue as it becomes recognisable. Composed here rather than inside
        // comic-reveal.js, which has no business knowing the app makes noise.
        onBand: (index, total) => {
            audio.panelReveal(index, total);
            haptics.tap();
        }
    }), 0),

    finishComicReveal: (id) => guard(() => comicReveal.finish(id)),

    // ── Reading the strip ────────────────────────────────────────────────────────────────

    /**
     * Plays the comic as it is read: one note of its own motif per panel, as that panel crosses
     * the middle of the screen.
     *
     * The note choice is composed here, not in panel-scrub.js — that module knows only that a
     * boundary was crossed. Deliberately NOT the same cue as the reveal's `panelReveal`: this is
     * the comic's identity, re-heard at the reader's own pace, so it is the signature's own
     * scale rather than a generic blip. `seed` is the place id for the same reason it always is.
     */
    startPanelScrub: (container, panels, seed, score) => guard(() => panelScrub.start(container, {
        panels: panels ?? 2,
        onPanel: (index, total) => {
            audio.panelNote(seed, score ?? 50, index, total);
        }
    }), 0),

    stopPanelScrub: (id) => guard(() => panelScrub.stop(id)),

    // ── Leaderboard ──────────────────────────────────────────────────────────────────────

    /**
     * Slides each row that moved since last visit from its old slot to its new one, then voices
     * the move where it happened. Rows are measured, not assumed: a stacked mobile row is three
     * times the height of a desktop one. Climbers ride above fallers so the winner is never
     * drawn underneath the row it passed.
     */
    animateBoardMoves: (container) => guard(() => {
        if (!container || !gfx.allows('lite')) return;
        const rows = [...container.querySelectorAll('[data-delta]')];
        let voiced = 0;
        rows.forEach((row, order) => {
            const delta = Number(row.dataset.delta);
            if (!delta) return;
            const rect = row.getBoundingClientRect();
            const height = rect.height;
            const delay = 250 + order * 70;
            const duration = 650 + Math.min(6, Math.abs(delta)) * 70;
            row.style.zIndex = delta > 0 ? '2' : '1';
            const animation = row.animate(
                [{ transform: `translateY(${delta * height}px)` }, { transform: 'none' }],
                { duration, delay, easing: 'cubic-bezier(0.22, 1, 0.36, 1)', fill: 'backwards' });
            animation.finished.then(() => { row.style.zIndex = ''; }).catch(() => { });
            // The cue and the buzz land as the row settles, not as it sets off: the arrival is
            // the news. The per-row delay is what keeps several movers from stacking into noise.
            const settle = delay + duration * 0.7;
            if (voiced++ < MAX_VOICED_MOVES) {
                audio.rankMove(delta, rect.left + rect.width / 2, settle / 1000);
            }
            if (Math.abs(delta) >= 3) setTimeout(() => guard(() => haptics.tap()), settle);
        });
    }),

    // ── Route transitions ────────────────────────────────────────────────────────────────
    /** Called after the destination route renders, to close the open transition. */
    settleViewTransition: () => guard(() => viewTransitions.settle())

};

window.poseeFx = fx;
