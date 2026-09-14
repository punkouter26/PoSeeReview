// The generative ambient bed: one worklet node, hosted in the app's existing audio graph.
//
// WHY A BED AT ALL. Every other sound in the app is an event — you did something, it answered.
// Between events the page is silent, which means the audio identity only exists at the moment of
// a tap. A bed makes the comic page a PLACE rather than a series of confirmations, and because
// it feeds the same analyser that drives the backdrop gradient, the visuals keep breathing when
// nothing is happening. That last part is the actual reason it earns its cost: audio-reactive.js
// already exists and had nothing to react to most of the time.
//
// WHY IT IS THE ONE SOUND THAT NEEDS A WORKLET. See the header of posee-synth-processor.js —
// briefly: a sustained tone whose parameters are written from the main thread clicks whenever
// the main thread is busy, and the main thread here is running Blazor renders and a shared rAF
// loop. Transients do not have this problem, which is why nothing else was moved.
//
// THREE RULES.
//
//  1. It follows the audio preference and has its own opt-out on top. A drone is more intrusive
//     than a click, so a user who wanted click feedback has not thereby asked for a drone.
//  2. It DUCKS. Every foreground voice pushes it down and it comes back slowly. A bed that is
//     mixed against the cues rather than under them makes the cues harder to hear, which is
//     exactly backwards.
//  3. It fades in and out over seconds. A bed that starts abruptly is a glitch; one that starts
//     over three seconds is a room you did not notice you had walked into.

import { audio } from './audio.js';

const STORAGE_KEY = 'posee_ambient_enabled';

// Ducking release. Long enough that a burst of ticks during the score count-up holds the bed
// down for the whole run rather than pumping between every note.
const DUCK_RELEASE_MS = 900;

const state = {
    // null = follow the audio preference; true/false = the user chose.
    preference: null,
    node: null,
    bus: null,
    moduleLoaded: false,
    loading: null,
    unsubscribeVoices: null,
    duckTimer: 0,
    running: false,
    score: 50,
    failed: false
};

function readStoredPreference() {
    try {
        const raw = localStorage.getItem(STORAGE_KEY);
        if (raw === 'true') return true;
        if (raw === 'false') return false;
        return null;
    } catch {
        return null;
    }
}

function allowed() {
    if (state.failed) return false;
    if (state.preference === false) return false;
    return audio.isEnabled();
}

/**
 * Root frequency for a score. Lower as it gets stranger — a bed that rises with the score would
 * compete with the count-up, which is already the thing climbing. Held to a narrow range: this
 * sits under everything and a bed that moves a fifth between comics reads as a mistake.
 */
function rootFor(score) {
    const strange = Math.min(1, Math.max(0, score / 100));
    return 73.42 - strange * 12;   // roughly D2 down to just under C2
}

/**
 * Loads the processor module. `import.meta.url` rather than a relative string: the app is a SPA
 * and this can be called from /comic/{placeId}, where a bare './posee-synth-processor.js' would
 * resolve against the route and 404 into the SPA fallback — which returns index.html with a 200,
 * so the failure would surface as a syntax error in a worklet rather than as a missing file.
 */
async function ensureModule(ctx) {
    if (state.moduleLoaded) return true;
    if (state.loading) return state.loading;

    if (!ctx.audioWorklet) {
        // Pre-2021 Safari. The bed is the most optional thing in the app and a node-graph
        // fallback would be a second implementation of the same sound to maintain, so this
        // degrades to silence and everything else carries on.
        state.failed = true;
        return false;
    }

    state.loading = (async () => {
        try {
            const url = new URL('./posee-synth-processor.js', import.meta.url);
            await ctx.audioWorklet.addModule(url);
            state.moduleLoaded = true;
            return true;
        } catch (err) {
            console.warn('[ambient] worklet unavailable; no bed', err);
            state.failed = true;
            return false;
        } finally {
            state.loading = null;
        }
    })();

    return state.loading;
}

function post(message) {
    try {
        state.node?.port.postMessage(message);
    } catch {
        // The node was torn down between the check and the post.
    }
}

/** Pushes the bed down, and schedules the release. Called for every foreground voice. */
function duck() {
    post({ duck: 1 });
    clearTimeout(state.duckTimer);
    state.duckTimer = setTimeout(() => post({ duck: 0 }), DUCK_RELEASE_MS);
}

/**
 * Starts the bed. Safe to call repeatedly — a second call just retunes the running node, which
 * is what a route re-entry or a regenerate should do.
 *
 * @returns {Promise<boolean>} whether a bed is now playing.
 */
export async function start(score = 50) {
    state.score = score;

    if (!allowed()) return false;

    const ctx = audio.context();
    // No context yet means audio has not been unlocked by a gesture. Not an error: the caller
    // will try again after the next tap, and starting a bed before the user has interacted is
    // exactly what the unlock rule exists to prevent.
    if (!ctx || ctx.state !== 'running') return false;

    if (state.running && state.node) {
        setScore(score);
        return true;
    }

    if (!(await ensureModule(ctx))) return false;

    // The context can be suspended again while addModule was in flight.
    if (!allowed() || ctx.state !== 'running') return false;

    try {
        state.bus = audio.createBus({ pan: 0, send: 0.5 });
        if (!state.bus) return false;

        state.node = new AudioWorkletNode(ctx, 'posee-ambient', {
            numberOfInputs: 0,
            numberOfOutputs: 1,
            outputChannelCount: [2],
            processorOptions: {
                root: rootFor(score),
                strange: Math.min(1, Math.max(0, score / 100))
            }
        });

        state.node.connect(state.bus.node);

        // Fade in. The processor smooths toward this over ~0.9s, and posting it on the next tick
        // rather than in processorOptions means the node has already produced a silent block
        // before the level starts moving — so the first audible sample is never a discontinuity.
        setTimeout(() => post({ level: 1 }), 40);

        state.unsubscribeVoices = audio.onVoice(duck);
        state.running = true;
        return true;
    } catch (err) {
        console.warn('[ambient] could not start', err);
        stop();
        return false;
    }
}

/** Retunes a running bed. Cheap, and the processor glides — call it as often as the score moves. */
export function setScore(score) {
    state.score = score;
    if (!state.running) return;
    post({
        root: rootFor(score),
        strange: Math.min(1, Math.max(0, score / 100))
    });
}

/**
 * Fades out and tears down. The disconnect is deferred until after the fade: cutting the node
 * loose while it is still producing sound is the click this whole module is built to avoid.
 */
export function stop() {
    clearTimeout(state.duckTimer);
    state.unsubscribeVoices?.();
    state.unsubscribeVoices = null;
    state.running = false;

    const node = state.node;
    const bus = state.bus;
    state.node = null;
    state.bus = null;
    if (!node) return;

    try {
        node.port.postMessage({ level: 0 });
    } catch { /* already gone */ }

    setTimeout(() => {
        try { node.port.postMessage({ dispose: true }); } catch { /* already gone */ }
        try { node.disconnect(); } catch { /* already gone */ }
        bus?.dispose();
    }, 1400);
}

export function isRunning() {
    return state.running;
}

export function describe() {
    return {
        supported: !state.failed,
        running: state.running,
        enabled: allowed(),
        explicit: state.preference !== null
    };
}

/** null returns to following the audio preference. */
export function setEnabled(enabled) {
    state.preference = enabled === null ? null : !!enabled;
    try {
        if (state.preference === null) {
            localStorage.removeItem(STORAGE_KEY);
        } else {
            localStorage.setItem(STORAGE_KEY, String(state.preference));
        }
    } catch {
        // Session-only.
    }

    if (!allowed()) {
        stop();
    }
    return allowed();
}

/** Called by fx.js when the audio preference changes, since the bed inherits from it. */
export function syncAudio(audioEnabled) {
    if (!audioEnabled || !allowed()) {
        stop();
    }
    return allowed();
}

export function init() {
    state.preference = readStoredPreference();
    return describe();
}
