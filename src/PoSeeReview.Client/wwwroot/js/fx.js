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

import { gfx, comicImageUrl } from './gfx-core.js';
import { audio } from './audio.js';
import { haptics } from './haptics.js';
import * as gradient from './gradient.js';
import * as panelScrub from './panel-scrub.js';
import * as comicReveal from './comic-reveal.js';
import * as viewTransitions from './view-transitions.js';
import * as confetti from './confetti.js';
import * as glass from './glass.js';
import { soundtrack } from './soundtrack.js';
import * as particles from './particles.js';
import * as halftone from './halftone-reveal.js';
import * as inkFluid from './ink-fluid.js';
import * as duelSeam from './duel-seam.js';
import * as pageCurl from './page-curl.js';
import * as depth from './depth-diorama.js';

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
guard(() => glass.init());

function propagateAudioState(enabled) {
    guard(() => haptics.syncAudio(enabled));
    if (!enabled) {
        guard(() => haptics.cancel());
        guard(() => soundtrack.halt());
    } else {
        // A gesture that brought sound back may be the first moment the wanted loop can play.
        guard(() => soundtrack.resume(audio.bus()));
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

// Reveal handles handed to .NET, each naming which implementation is running behind it.
const reveals = new Map();
let nextReveal = 1;
function remember(kind, inner) {
    const id = nextReveal++;
    reveals.set(id, { kind, inner });
    return id;
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
    /** "Too normal to draw": a deflating sad trombone. A joke about the place, not an error. */
    playFizzle: () => guard(() => audio.fizzle()),
    /** The map flying to a tapped pin; called by map-view.js through this surface. */
    playMapFly: (clientX) => guard(() => audio.whoosh(clientX)),

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

    // ── Generation wait ──────────────────────────────────────────────────────────────────

    /** Ink blooming behind the storyboard while the comic is drawn. Full tier; 0 elsewhere. */
    startInk: (canvas) => guard(() => inkFluid.start(canvas, {
        onEnergy: (energy) => guard(() => audio.inkSwell(energy))
    }), 0),
    /** Each pipeline phase squirts its own colour from its own side. */
    inkPhase: (handle, index, total) => guard(() => {
        inkFluid.phase(handle, index, total);
        haptics.tap();
    }),
    /** The comic has arrived: the ink spirals into the frame over ~700ms and is gone. */
    drainInk: (handle) => guard(() => {
        inkFluid.drain(handle);
        audio.inkDrain();
    }),
    stopInk: (handle) => guard(() => inkFluid.stop(handle)),

    // ── Score burst ──────────────────────────────────────────────────────────────────────

    /** Compiles the burst's GPU pipelines ahead of time, so they are not built as the score lands. */
    prepareBurst: () => guardAsync(() => particles.describe(), { webgpu: false }),

    /**
     * Particles from the score ring as the count-up lands, sized by the score. At the flash peak a
     * score above 50 also sends a ring through the backdrop and drops a sub-bass thump under the
     * landing chord; the haptic is the count-up's own, so none is added here.
     */
    burstScore: (origin, score) => guardAsync(() => particles.burst(origin, {
        score,
        onPeak: () => guard(() => {
            if (score <= 50) return;
            const rect = origin.getBoundingClientRect();
            gradient.shock(rect.left + rect.width / 2, rect.top + rect.height / 2, score > 80 ? 1 : 0.6);
            audio.thump(score);
        })
    }), 0),

    /** "Too normal": grey motes pop off the card's icon and flop to the floor. Silent; see playFizzle. */
    fizzleAt: (selector) => guard(() => particles.fizzle(document.querySelector(selector)), 0),

    // ── Soundtrack (opt-in) ──────────────────────────────────────────────────────────────
    soundtrackEnabled: () => guard(() => soundtrack.isEnabled(), false),
    /** From the account menu's click, so the context can unlock on the same gesture. */
    setSoundtrackEnabled: (enabled) => guardAsync(async () => {
        const applied = soundtrack.setEnabled(enabled);
        if (applied) {
            await audio.unlock();
            soundtrack.resume(audio.bus());
        }
        return applied;
    }, false),
    /** The comic page asks for its loop; it starts now if it can, or when sound next unlocks. */
    startSoundtrack: (seed, score) => guard(() => soundtrack.want(audio.bus(), seed, score)),
    stopSoundtrack: () => guard(() => soundtrack.release()),

    // ── Haptics ──────────────────────────────────────────────────────────────────────────
    hapticsDescribe: () => guard(() => haptics.describe(), { supported: false, enabled: false, explicit: false }),
    setHapticsEnabled: (enabled) => guard(() => {
        const active = haptics.setEnabled(enabled);
        if (active) toggleTick();
        return active;
    }, false),

    // ── Narration ────────────────────────────────────────────────────────────────────────
    canNarrate: () => guard(() => audio.canNarrate(), false),
    narrate: (text) => guard(() => {
        const speaking = audio.narrate(text);
        if (speaking) {
            soundtrack.duck(true);
            audio.narrationEnded().then(() => guard(() => soundtrack.duck(false)));
        }
        return speaking;
    }, false),
    stopNarration: () => guard(() => audio.stopNarration()),
    /** Resolves when the current utterance is over, or at once if nothing is speaking. */
    narrationEnded: () => guardAsync(() => audio.narrationEnded()),

    // ── Background gradient ──────────────────────────────────────────────────────────────
    // The backdrop's audio meter is handed over here: gradient.js does not import audio.js.
    startGradient: (canvas, score) => guard(() => gradient.start(canvas, { score, level: audio.level }), 0),
    setGradientScore: (id, score) => guard(() => gradient.setScore(id, score)),
    stopGradient: (id) => guard(() => gradient.stop(id)),

    // ── Ink development ──────────────────────────────────────────────────────────────────

    /**
     * Develops the comic onto the page. At the full tier it is PRINTED: CMYK plates landing in
     * register over the same-origin copy of the artwork (halftone-reveal.js). Anywhere that
     * cannot run — lower tier, no placeId, the image not arriving — the CSS mask develops it.
     * The returned handle remembers which of the two is running.
     */
    startComicReveal: (container, bands, placeId) => guardAsync(async () => {
        // Each band gets a cue as it becomes recognisable. Composed here rather than inside
        // either reveal, which have no business knowing the app makes noise.
        const onBand = (index, total) => {
            audio.panelReveal(index, total);
            haptics.tap();
        };
        if (placeId && gfx.allows('full')) {
            const printed = await halftone.start(container, {
                src: comicImageUrl(placeId),
                bands: bands ?? 2,
                onBand
            });
            if (printed) return remember('print', printed);
        }
        const developed = comicReveal.start(container, { bands: bands ?? 2, onBand });
        return developed ? remember('mask', developed) : 0;
    }, 0),

    finishComicReveal: (id) => guard(() => {
        const entry = reveals.get(id);
        reveals.delete(id);
        if (entry?.kind === 'print') halftone.finish(entry.inner);
        else if (entry) comicReveal.finish(entry.inner);
    }),

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
        const bodies = [];
        let voiced = 0;
        rows.forEach((row, order) => {
            const delta = Number(row.dataset.delta);
            if (!delta) return;
            const rect = row.getBoundingClientRect();
            const delay = 250 + order * 70;
            row.style.zIndex = delta > 0 ? '2' : '1';
            // Each mover is a damped spring pulled from its old slot to its new one. A climber
            // is launched with an extra upward kick so it overshoots its new rank and drops back
            // in; a faller is a heavier body that lands short, bounces, and wobbles.
            bodies.push({
                row, delta, delay,
                y: delta * rect.height,
                v: delta > 0 ? -Math.min(6, delta) * 260 : 0,
                stiffness: delta > 0 ? 210 : 260,
                damping: delta > 0 ? 15 : 11,
                started: false,
                landed: false,
                clientX: rect.left + rect.width / 2,
                voice: voiced++ < MAX_VOICED_MOVES
            });
            row.style.transform = `translateY(${delta * rect.height}px)`;
        });
        if (bodies.length === 0) return;

        // A climb into first place is a coronation: confetti from the row, and a fanfare.
        const champion = rows[0];
        const crowned = Number(champion?.dataset.delta) > 0;

        const t0 = performance.now();
        let last = t0;
        const stop = gfx.addTask('board-springs', (now) => {
            const dt = Math.min(1 / 30, (now - last) / 1000);
            last = now;
            let moving = 0;
            for (const body of bodies) {
                if (now - t0 < body.delay) {
                    moving++;
                    continue;
                }
                if (!body.row.isConnected) continue;
                const force = -body.stiffness * body.y - body.damping * body.v;
                body.v += force * dt;
                body.y += body.v * dt;

                // The cue and the buzz land as the row first reaches its slot, not as it sets
                // off: the arrival is the news. The staggered delays keep movers from stacking.
                const arrived = body.delta > 0 ? body.y <= 0 : body.y >= 0;
                if (arrived && !body.landed) {
                    body.landed = true;
                    if (body.voice) audio.rankMove(body.delta, body.clientX, 0);
                    if (Math.abs(body.delta) >= 3) haptics.tap();
                    if (crowned && body.row === champion) {
                        confetti.burst(champion);
                        audio.crown(body.clientX);
                        haptics.confirm();
                    }
                }

                const settled = Math.abs(body.y) < 0.4 && Math.abs(body.v) < 6;
                if (settled) {
                    body.row.style.transform = '';
                    body.row.style.zIndex = '';
                    continue;
                }
                moving++;
                // A little lean in the direction of travel sells the mass.
                const lean = Math.max(-2.5, Math.min(2.5, body.v * 0.0035));
                const lift = body.delta > 0 ? 1 + Math.min(0.03, Math.abs(body.v) * 0.00002) : 1;
                body.row.style.transform = `translateY(${body.y.toFixed(2)}px) rotate(${lean.toFixed(3)}deg) scale(${lift.toFixed(4)})`;
            }
            if (moving === 0 || now - t0 > 6000) {
                for (const body of bodies) {
                    body.row.style.transform = '';
                    body.row.style.zIndex = '';
                }
                stop();
            }
        });
    }),

    // ── Hot off the press ────────────────────────────────────────────────────────────────

    /** Builds the curl's GL context while a share sheet is open, so the peel starts at once. */
    warmPeel: () => guardAsync(() => pageCurl.warm(), false),

    /**
     * After a share or save: a printed copy of the comic curls off the page and flies into the
     * button that sent it, and a CERTIFIED STRANGE stamp slams onto the original. Lite tier gets
     * the stamp alone; off gets nothing.
     */
    peelComic: (placeId, targetSelector) => guardAsync(() => {
        const container = document.querySelector('.comic-strip-container');
        if (!container) return 0;
        return pageCurl.peel(container, {
            src: comicImageUrl(placeId),
            target: targetSelector ? document.querySelector(targetSelector) : null,
            onLift: () => guard(() => audio.paperLift()),
            onStamp: () => guard(() => {
                audio.pressClunk();
                haptics.confirm();
            })
        });
    }, 0),

    // ── 3D mode ──────────────────────────────────────────────────────────────────────────

    depthSupported: () => guard(() => depth.supported(), false),

    /**
     * Turns the strip into a tilting 2.5D diorama. Heuristic depth at once; on WebGPU a depth
     * model (~25MB, first use only) loads in a worker and blends in. `dotnet` hears DepthEnded
     * when the effect stops by itself (tier drop, lost context), so the toggle can reset.
     */
    startDepth: (placeId, dotnet) => guardAsync(async () => {
        const container = document.querySelector('.comic-strip-container');
        if (!container) return 0;
        const handle = await depth.start(container, {
            src: comicImageUrl(placeId),
            onProgress: (fraction, label) => guard(() => {
                if (fraction >= 1 && label === 'model') {
                    audio.confirm();
                    haptics.confirm();
                }
            }),
            onEnd: () => dotnet?.invokeMethodAsync('DepthEnded').catch(() => { })
        });
        if (handle) {
            const rect = container.getBoundingClientRect();
            audio.whoosh(rect.left + rect.width / 2);
            haptics.tap();
        }
        return handle;
    }, 0),

    stopDepth: (handle) => guard(() => {
        if (!handle) return;
        depth.stop(handle);
        audio.navigate('back', window.innerWidth / 2);
    }),

    // ── Duel ─────────────────────────────────────────────────────────────────────────────

    /**
     * The arc between the duel card's halves: ignition crack, the strike's thump (where the
     * page's CSS shake lands), then each comic's motif from its own side and the winner's chord.
     */
    startDuel: (canvas, seedA, scoreA, seedB, scoreB) => guard(() => duelSeam.start(canvas, {
        scoreA,
        scoreB,
        onIgnite: () => guard(() => {
            audio.duelIgnite();
            haptics.tap();
        }),
        onStrike: () => guard(() => {
            audio.duelStrike();
            haptics.confirm();
        }),
        onWinner: (side) => guard(() => audio.duelWinner(side, seedA, scoreA, seedB, scoreB))
    }), 0),
    stopDuel: (handle) => guard(() => duelSeam.stop(handle)),

    // ── Route transitions ────────────────────────────────────────────────────────────────
    /** Called after the destination route renders, to close the open transition. */
    settleViewTransition: () => guard(() => viewTransitions.settle())

};

window.poseeFx = fx;
