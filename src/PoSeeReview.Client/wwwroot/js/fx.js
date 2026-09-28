// Single entry point for everything in js/. Loaded as a module from index.html; publishes one
// flat `window.poseeFx` surface because that is how the existing interop in this app works
// (window.geolocation, window.shareUtils) and mixing two conventions helps nobody.
//
// Every method here is defensive. Blazor calls these from component lifecycle methods, and a
// throw inside JS interop surfaces to the user as the framework's red error strip — which for
// decoration is a spectacularly bad trade. Nothing in this file may ever throw into .NET.
//
// THIS FILE IS ALSO THE COMPOSITION ROOT for the effects that pair with each other. audio.js
// does not know haptics exist, overlays.js does not know stamps make a noise, and
// comic-surface.js does not know the loupe hums. Deciding that a tap should also buzz, or that a
// weird page should also drone, is a product decision and it is made here.

import { gfx } from './gfx-core.js';
import { audio } from './audio.js';
import { haptics } from './haptics.js';
import * as gradient from './gradient.js';
import * as panelScrub from './panel-scrub.js';
import * as comicReveal from './comic-reveal.js';
import * as particles from './particles.js';
import * as viewTransitions from './view-transitions.js';
import * as comicSurface from './comic-surface.js';
import * as overlays from './overlays.js';

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

gfx.onTierChanged(() => {
    if (!audio.isEnabled()) {
        propagateAudioState(false);
    }
});

/** Loupe hums, keyed by comic-surface handle. Sustained, so each must be released. */
const loupeHums = new Map();

const panForX = (clientX) => Math.max(-0.7, Math.min(0.7, (clientX / (window.innerWidth || 1)) * 1.4 - 0.7));

export const fx = {
    // ── Capability + tier ────────────────────────────────────────────────────────────────
    describe: () => guard(() => ({
        ...gfx.describe(),
        webgpu: typeof navigator !== 'undefined' && 'gpu' in navigator,
        haptics: haptics.describe().supported,
        narration: audio.canNarrate()
    }), {
        tier: 'off', reducedMotion: true, webgl2: false, autoDowngraded: false,
        webgpu: false, haptics: false, narration: false
    }),
    setTier: (tier) => guard(() => gfx.setTier(tier), 'off'),
    stats: () => guard(() => gfx.stats(), null),
    resetStats: () => guard(() => gfx.resetStats()),

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
    /**
     * The discovery beats. These exist because the landing page — the first screen every visitor
     * sees — had exactly two cues on it: a tap, and the audio unlock hung off the same handler.
     * Asking for your location, waiting, and getting fifteen restaurants back all sounded
     * identical to pressing a button.
     */
    playLocating: () => guard(() => {
        audio.locating();
        haptics.locating();
    }),
    playArrival: (count) => guard(() => {
        audio.arrival(count ?? 0);
        haptics.arrival(count ?? 0);
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
            haptics.tapUncached();
        }
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

    /**
     * The top of the leaderboard as a chord — each voice one restaurant's own motif.
     *
     * `/leaderboard` had a 3D shelf and not one sound on it. Because a motif is deterministic
     * from its place id, this makes a board that has CHANGED audibly different from one that has
     * not, before a single row has been read.
     */
    playBoardChord: (entries) => guard(() => audio.boardChord(entries ?? [])),

    /**
     * A row that moved since this visitor last saw the board. Panned to the row, and small: this
     * plays while someone is reading, so it must be a detail being pointed at.
     */
    playRankDelta: (delta, pan) => guard(() => {
        audio.rankDelta(delta ?? 0, pan ?? 0);
        if (Math.abs(delta ?? 0) >= 3) haptics.tap();
    }),

    /** Plays a numeric series as pitch. Used by /insights to make a chart's shape audible. */
    playSeries: (values, options) => guard(() => audio.sonify(values ?? [], options ?? {})),

    // ── Haptics ──────────────────────────────────────────────────────────────────────────
    hapticsDescribe: () => guard(() => haptics.describe(), { supported: false, enabled: false, explicit: false }),
    setHapticsEnabled: (enabled) => guard(() => haptics.setEnabled(enabled), false),

    // ── Narration ────────────────────────────────────────────────────────────────────────
    canNarrate: () => guard(() => audio.canNarrate(), false),
    narrate: (text) => guard(() => audio.narrate(text), false),
    stopNarration: () => guard(() => {
        audio.stopNarration();
        overlays.clearBubbles();
    }),

    /**
     * The skit rides the same speechSynthesis output and the same stopNarration() stop. Each
     * line pops a bubble over the strip as it starts — the voices get faces — and the bubble
     * pops from its speaker's side of the stereo field as well as the strip.
     */
    playSkit: (json, strip) => guard(() => {
        const speech = overlays.bubbles(strip, { panels: 2 });
        return audio.playSkitJson(json, speech ? {
            onLineStart: (index, total, speaker, slot, text) => guard(() => {
                const side = speech.show(index, total, speaker, slot, text);
                audio.bubble(side === 'left' ? -0.45 : 0.45);
            }),
            onLineEnd: (index, total) => guard(() => speech.end(index, total))
        } : {});
    }, false),

    // ── Generation wait ──────────────────────────────────────────────────────────────────
    riserStep: (index, total, score) => guard(() => {
        audio.riserStep(index, total, score ?? 0);
        haptics.phase();
    }),
    riserResolve: () => guard(() => audio.riserResolve()),
    riserStop: () => guard(() => audio.riserStop()),

    // ── Background gradient ──────────────────────────────────────────────────────────────
    startGradient: (canvas, score) => guard(() => gradient.start(canvas, { score }), 0),
    setGradientScore: (id, score) => guard(() => gradient.setScore(id, score)),
    stopGradient: (id) => guard(() => gradient.stop(id)),

    /**
     * Eases the backdrop to a comic's own colours. `palette` is the three hex strings the server
     * sampled off the finished artwork; anything else clears back to the brand gradient, which is
     * what a comic drawn before the extractor existed still gets.
     */
    setComicPalette: (id, palette) => guard(() => {
        const applied = Array.isArray(palette) && palette.length > 0;
        gradient.setPalette(id, applied ? palette : null);
        return applied;
    }, false),

    /** Restores the brand gradient. Called on leaving a comic route. */
    clearComicPalette: (id) => guard(() => gradient.setPalette(id, null)),

    /**
     * Viewport width, for callers that need to convert a pointer coordinate into a fraction of
     * a full-width canvas. Here rather than as a raw JS eval on the .NET side so it goes through
     * the same guard as everything else and cannot throw into interop.
     */
    viewportWidth: () => guard(() => window.innerWidth || 1, 1),

    // ── Ink burst ────────────────────────────────────────────────────────────────────────
    burstParticles: (canvas, score) => guard(() => particles.burst(canvas, score ?? 50), 0),
    stopParticles: (id) => guard(() => particles.dispose(id)),

    // ── The comic as an object ───────────────────────────────────────────────────────────

    /** Foil (when `holo`) and loupe support. The foil glints audibly as it is tilted. */
    startComicSurface: (container, image, holo) => guard(() => {
        let id = 0;
        id = comicSurface.start(container, image, {
            holo: holo === true,
            onShimmer: (intensity, pan) => guard(() => audio.shimmer(intensity, pan)),
            // The hum follows the lens: pitch rises toward the top, pan tracks it across.
            onLoupeMove: (x, y) => guard(() => loupeHums.get(id)?.glide(140 + (1 - y) * 300, x * 1.4 - 0.7))
        });
        return id;
    }, 0),

    toggleLoupe: (id) => guard(() => {
        const on = comicSurface.toggleLoupe(id);
        loupeHums.get(id)?.release(0.2);
        loupeHums.delete(id);
        if (on) {
            const hum = audio.hum();
            if (hum) loupeHums.set(id, hum);
        }
        haptics.tap();
        return on;
    }, false),

    wobbleComic: (id, score) => guard(() => comicSurface.wobble(id, score ?? 50)),

    stopComicSurface: (id) => guard(() => {
        loupeHums.get(id)?.release(0.1);
        loupeHums.delete(id);
        comicSurface.stop(id);
    }),

    /** The page misbehaves, and sounds like it. The drone is part of the same decision. */
    setWeird: (score) => guard(() => {
        if (comicSurface.setWeird(score ?? 0)) audio.droneStart(score);
    }),

    clearWeird: () => guard(() => {
        comicSurface.clearWeird();
        audio.droneStop();
    }),

    /**
     * A comic-book sound word. `target` is an element, or a viewport x with `y` beside it.
     * The thwack is panned to wherever it landed.
     */
    stamp: (word, target, y) => guard(() => {
        const x = overlays.stamp(word, target, y);
        if (x === null) return;
        audio.stamp(panForX(x));
        haptics.splat(0.4);
    }),

    // ── Ink development ──────────────────────────────────────────────────────────────────

    /**
     * Develops the comic onto the page. `fxHandle` is the comic-fx handle when one attached, so
     * the shader can add its wet-ink boundary on top of the CSS mask; pass 0 and the CSS mask
     * runs alone, which is the common case.
     */
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
        rows.forEach((row, order) => {
            const delta = Number(row.dataset.delta);
            if (!delta) return;
            const height = row.getBoundingClientRect().height;
            const delay = 250 + order * 70;
            const duration = 650 + Math.min(6, Math.abs(delta)) * 70;
            row.style.zIndex = delta > 0 ? '2' : '1';
            const animation = row.animate(
                [{ transform: `translateY(${delta * height}px)` }, { transform: 'none' }],
                { duration, delay, easing: 'cubic-bezier(0.22, 1, 0.36, 1)', fill: 'backwards' });
            animation.finished.then(() => { row.style.zIndex = ''; }).catch(() => { });
            // The cue lands as the row settles, not as it sets off: the arrival is the news.
            setTimeout(() => guard(() => {
                audio.rankDelta(delta, delta > 0 ? 0.4 : -0.4);
                if (Math.abs(delta) >= 3) haptics.tap();
            }), delay + duration * 0.7);
        });
    }),

    // ── Route transitions ────────────────────────────────────────────────────────────────
    /** Called after the destination route renders, to close the open transition. */
    settleViewTransition: () => guard(() => viewTransitions.settle())

};

window.poseeFx = fx;
