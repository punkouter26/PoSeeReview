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
import * as comicTint from './comic-tint.js';
import * as glass from './glass.js';
import * as paper from './paper.js';
import * as panelScrub from './panel-scrub.js';
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
/**
 * Resolves to the wet-ink compute module, or null without WebGPU. Probed before the import for
 * the same reason particles-gpu is: a device that cannot run WGSL never fetches any.
 */
let inkFieldModule = null;

async function loadInkField() {
    if (inkFieldModule !== null) {
        return inkFieldModule;
    }
    try {
        const { isSupported } = await import('./webgpu-pool.js');
        if (!isSupported()) {
            inkFieldModule = false;
            return false;
        }
        inkFieldModule = await import('./ink-field.js');
        return inkFieldModule;
    } catch {
        inkFieldModule = false;
        return false;
    }
}

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
// The navigation cue is composed here, not inside view-transitions.js: that module must not
// learn that the app makes noise, on the same terms gradient.js must not learn about the
// analyser. A page turn that sweeps in the direction of travel is a product decision.
viewTransitions.init({
    onNavigate: (direction, clientX) => guard(() => audio.navigate(direction, clientX))
});
guard(() => initPerfHud());
// One small texture, built once and handed to CSS as a custom property. Not an effect: nothing
// is registered with the scheduler, and after this the paper ageing on /my-comics is ordinary
// painting. Installed here rather than lazily on that route so the first render of the grid
// already has it — a grain that fades in after the cards reads as a loading glitch.
guard(() => paper.install());

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

// The other half of the pane/backdrop pairing enforced in startGlass below. A gradient can stop
// for reasons that have nothing to do with the tier — a lost context is the realistic one — and
// a pane that kept running would then be refracting a scene the page is no longer showing.
// Composed here because gradient.js must not know panes exist and glass.js must not know the
// backdrop has a CSS fallback.
gradient.onActiveChanged((count) => {
    if (count === 0) {
        guard(() => glass.stopAll());
    }
});

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

// The current scene, mirrored here because glass panes are started and stopped independently of
// the backdrop and have to be able to catch up. A pane mounted after the score landed would
// otherwise refract a default-coloured scene while the page behind it shows the comic's own —
// the one failure mode that makes glass look like a bug rather than a material.
let lastScore = 40;
let lastPalette = null;

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
    stopNarration: () => guard(() => audio.stopNarration()),
    // The skit rides the same speechSynthesis output and the same stopNarration() stop.
    playSkit: (json) => guard(() => audio.playSkitJson(json), false),

    // ── Ambient bed ──────────────────────────────────────────────────────────────────────
    startAmbient: (score) => guardAsync(() => ambient.start(score ?? 50), false),
    setAmbientScore: (score) => guard(() => ambient.setScore(score)),
    stopAmbient: () => guard(() => ambient.stop()),
    ambientDescribe: () => guard(() => ambient.describe(), { supported: false, running: false, enabled: false, explicit: false }),
    setAmbientEnabled: (enabled) => guard(() => ambient.setEnabled(enabled), false),

    // ── Background gradient ──────────────────────────────────────────────────────────────
    startGradient: (canvas, score) => guard(() => gradient.start(canvas, { score }), 0),
    setGradientScore: (id, score) => guard(() => {
        lastScore = score ?? lastScore;
        gradient.setScore(id, score);
        glass.setScene(lastScore, lastPalette);
    }),
    stopGradient: (id) => guard(() => {
        // A gradient going away takes the tint with it, or the next route inherits the colours
        // of a comic that is no longer on screen.
        comicTint.clear();
        gradient.stop(id);
    }),

    // ── Comic palette ────────────────────────────────────────────────────────────────────
    //
    // Composed here, not in either module, because deciding that a comic's colours reach BOTH
    // the shader backdrop and the CSS accents is a product decision — the same reason a tap
    // also buzzes. gradient.js knows nothing about CSS variables and comic-tint.js knows
    // nothing about WebGL; either alone is a half-tinted page.

    /**
     * Dresses the app in a comic's own colours. `palette` is the three hex strings the server
     * sampled off the finished artwork. Anything else clears back to the brand gradient, which
     * is what a comic drawn before the extractor existed still gets.
     */
    setComicPalette: (id, palette) => guard(() => {
        const applied = comicTint.apply(palette);
        lastPalette = applied ? palette : null;
        gradient.setPalette(id, palette);
        // Panes refract INTO the backdrop, so a retint that reached only the backdrop would put
        // every glass card visibly out of register with the page behind it.
        glass.setScene(lastScore, palette);
        return applied;
    }, false),

    /** Restores the brand gradient. Called on leaving a comic route. */
    clearComicPalette: (id) => guard(() => {
        comicTint.clear();
        lastPalette = null;
        gradient.setPalette(id, null);
        glass.setScene(lastScore, null);
    }),

    // ── Refractive glass ─────────────────────────────────────────────────────────────────

    /**
     * Turns the canvas's PARENT into a real glass pane — refraction, edge dispersion, a
     * travelling highlight. Taking the parent rather than a second element reference is what
     * keeps the Blazor side to a single component with no id plumbing.
     *
     * A new pane is handed the current scene immediately: one mounted after a score landed would
     * otherwise refract a default-coloured backdrop while the page behind it shows the comic's,
     * which reads as a rendering bug rather than as glass.
     */
    startGlass: (canvas, thickness, tint, sheen) => guard(() => {
        const target = canvas?.parentElement;
        if (!target) return 0;

        // THE PANE AND THE BACKDROP ARE A PAIR, and this guard is the reason the pairing is
        // enforced here rather than inside glass.js.
        //
        // A pane refracts the PROCEDURAL backdrop — it recomputes the scene rather than reading
        // pixels. So if gradient.js is not actually running, what the page is showing is the CSS
        // fallback on .fx-backdrop, and the pane would faithfully refract a scene that is not on
        // screen. That is not a subtle degradation: it puts a differently-coloured rectangle in
        // the middle of the page. (Observed exactly once, when the frame-budget watchdog
        // downgraded the backdrop to 'lite' while a pane started at 'full'.)
        //
        // gradient.js must not learn that glass exists, and glass.js must not learn that the
        // backdrop has a CSS fallback. Composing them is this file's job.
        if (gradient.activeIds().length === 0) {
            return 0;
        }

        const id = glass.start(canvas, target, { thickness, tint, sheen });
        if (id) {
            glass.setScene(lastScore, lastPalette);
        }
        return id;
    }, 0),

    stopGlass: (id) => guard(() => glass.stop(id)),

    glassPanes: () => guard(() => glass.activeCount(), 0),

    /**
     * Viewport width, for callers that need to convert a pointer coordinate into a fraction of
     * a full-width canvas. Here rather than as a raw JS eval on the .NET side so it goes through
     * the same guard as everything else and cannot throw into interop.
     */
    viewportWidth: () => guard(() => window.innerWidth || 1, 1),

    comicTintApplied: () => guard(() => comicTint.isApplied(), false),

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

    /**
     * The reveal, preferring the simulated wet-ink boundary.
     *
     * Two genuinely different effects, not two qualities of one. The CSS mask is a function of
     * POSITION — the boundary looks the way it does because of where it is. The field is a
     * function of HISTORY: ink wicks along the grain, runs ahead of itself where the paper is
     * thirsty, and pools at the edge, because each cell reads what its neighbours did last step.
     * That needs state, and state per cell per frame is what a compute pass is for.
     *
     * They are mutually exclusive. The field COVERS the comic in paper and eats the cover away;
     * running the mask as well would develop the artwork twice, with a visible seam wherever the
     * two boundaries disagreed. So the mask is suppressed only once a field has actually started.
     *
     * Async because the device is acquired on demand. A zero from the field is the ordinary case
     * — no WebGPU, or below the full tier — and it falls through to exactly what shipped before.
     */
    startComicRevealField: (container, canvas, bands, fxHandle) => guardAsync(async () => {
        const inkField = await loadInkField();
        const fieldId = inkField ? await inkField.start(canvas) : 0;

        const revealId = comicReveal.start(container, {
            bands: bands ?? 2,
            fxHandle: fxHandle ?? 0,
            suppressMask: fieldId !== 0,
            onProgress: fieldId ? (progress) => inkField.setProgress(fieldId, progress) : null,
            onBand: (index, total) => {
                audio.panelReveal(index, total);
                haptics.tap();
            }
        });

        // A field with no reveal driving it would sit at front = 0 for ever: a comic permanently
        // covered in blank paper. That is the one failure here a user would actually notice, so
        // it is torn down rather than left running.
        if (!revealId && fieldId) {
            inkField.stop(fieldId);
            return 0;
        }

        if (!revealId) return 0;

        const handle = nextCompositeHandle++;
        backendHandles.set(handle, { kind: 'reveal', id: revealId, fieldId });
        return handle;
    }, 0),

    finishComicReveal: (id) => guardAsync(async () => {
        const entry = backendHandles.get(id);
        if (!entry) {
            // A handle from the plain startComicReveal, which returns comic-reveal's own id.
            comicReveal.finish(id);
            return;
        }

        backendHandles.delete(id);
        comicReveal.finish(entry.id);
        if (entry.fieldId) {
            const inkField = await loadInkField();
            inkField?.stop(entry.fieldId);
        }
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
    /**
     * Opens a persistent pile on a canvas — empty until `throwReaction` feeds it.
     *
     * Deliberately a separate entry point from settleInk. That one is a one-shot that seeds
     * itself, runs for about three seconds and tears itself down; this is a surface that stays
     * for the life of the page and accumulates. Folding them into one call would mean a `mode`
     * that silently changes the lifetime of the handle it returns.
     */
    startPile: (canvas) => guardAsync(async () => {
        const physics = await loadPhysics();
        const id = physics.start(canvas, { mode: 'pile' });
        if (!id) return 0;
        const handle = nextCompositeHandle++;
        backendHandles.set(handle, { kind: 'physics', id });
        return handle;
    }, 0),

    /**
     * Throws a reaction onto the pile, with the sound and the buzz that go with it.
     *
     * Composed here rather than in ReactionBar because it is three modules agreeing: the body is
     * physics, the click is audio panned to the tap, and the buzz is haptics. That pairing is a
     * product decision and this file is where those are made.
     */
    throwReaction: (handle, glyph, originX, originY, clientX) => guardAsync(async () => {
        const entry = backendHandles.get(handle);
        if (!entry || entry.kind !== 'physics') return false;
        const physics = await loadPhysics();
        const thrown = physics.emit(entry.id, {
            glyph,
            originX: originX ?? 0.5,
            originY: originY ?? 0.1
        });
        if (thrown) {
            audio.tapAt(clientX ?? window.innerWidth / 2);
            haptics.tap();
        }
        return thrown;
    }, false),

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
