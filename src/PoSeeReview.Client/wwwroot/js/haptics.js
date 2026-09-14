// Haptic feedback, derived from the same envelopes that drive the synth voices.
//
// WHY THIS EXISTS. Everything else in js/ is seen or heard. Nothing is felt. On a phone — which
// is what this app is built for — a vibration is the only channel that survives a muted device,
// a noisy room and a pocket. It costs zero bytes, zero GPU and no frame budget.
//
// WHY IT FOLLOWS THE AUDIO PREFERENCE. A user who muted the app did not ask to be buzzed
// instead; silencing one output and having another appear in its place is the opposite of what
// they asked for. So haptics default to the audio preference and can then be turned off on their
// own — never on on their own.
//
// THE PATTERN COMES FROM THE ENVELOPE, NOT FROM TASTE. navigator.vibrate takes alternating
// on/off millisecond runs and nothing else — no amplitude. The only expressive dimensions are
// duration and rhythm, so `fromEnvelope` converts a voice's attack/decay/peak into a run length
// (energy) and voices with a `delay` become separate runs at the right offset. That way the
// score chord, the splat and the share stinger are FELT with the shape they are heard with,
// rather than three identical buzzes.
//
// EVERY CALL IS A NO-OP ON FAILURE. iOS Safari exposes no Vibration API at all, some browsers
// require transient user activation and throw otherwise, and a user can disable it at the OS
// level. None of those is an error worth reporting.

const STORAGE_KEY = 'posee_haptics_enabled';

// A vibration long enough to be unpleasant rather than informative. Nothing here should ever
// approach it; the clamp exists so a bad intensity argument cannot buzz someone's leg for a
// second and a half.
const MAX_RUN_MS = 90;
const MAX_TOTAL_MS = 400;

const state = {
    supported: false,
    // null = "follow the audio preference". true/false = the user chose explicitly.
    preference: null,
    audioEnabled: false,
    lastFiredAt: new Map()
};

function detectSupport() {
    try {
        return typeof navigator !== 'undefined' && typeof navigator.vibrate === 'function';
    } catch {
        return false;
    }
}

function readStoredPreference() {
    try {
        const raw = localStorage.getItem(STORAGE_KEY);
        if (raw === 'true') return true;
        if (raw === 'false') return false;
        return null;
    } catch {
        return null; // Private mode. Follow audio for the session.
    }
}

/** Haptics are on when supported, not explicitly refused, and audio is on. */
function active() {
    if (!state.supported) return false;
    if (state.preference === false) return false;
    return state.preference === true ? true : state.audioEnabled;
}

/** Same shape as the audio throttle: rapid repeats thin out rather than stacking. */
function throttled(key, minGapMs) {
    const now = performance.now();
    const last = state.lastFiredAt.get(key) ?? -Infinity;
    if (now - last < minGapMs) {
        return true;
    }
    state.lastFiredAt.set(key, now);
    return false;
}

/**
 * Sends a pattern, clamping every run and the total. Returns false for anything that did not
 * actually reach the device, so callers can fall back — though none currently need to.
 */
function fire(pattern) {
    if (!active()) return false;

    const runs = [];
    let total = 0;
    for (const value of pattern) {
        const clamped = Math.max(0, Math.min(MAX_RUN_MS, Math.round(value)));
        if (total + clamped > MAX_TOTAL_MS) break;
        total += clamped;
        runs.push(clamped);
    }
    if (runs.length === 0) return false;

    // A trailing gap is meaningless and some engines warn about it.
    while (runs.length > 1 && runs.length % 2 === 0) runs.pop();

    try {
        return navigator.vibrate(runs) !== false;
    } catch {
        // Requires transient activation in some engines and throws without it. Not an error.
        return false;
    }
}

/**
 * Converts a list of scheduled voices into a vibration pattern.
 *
 * Each entry is `{ delay, attack, decay, peak }` — exactly the fields audio.js's `voice()` takes,
 * so a cue can be described once and both played and felt. Duration of a run is the note's
 * audible energy (peak x envelope length) mapped into the perceptible 8-60ms band; the gap before
 * it is the difference between its delay and the end of the previous run.
 *
 * Runs shorter than ~8ms are inaudible to skin — most phones cannot start and stop the motor that
 * fast — so anything below the floor is raised to it rather than dropped, which keeps the rhythm
 * of a fast figure intact even where its individual notes blur together.
 */
export function fromEnvelope(voices) {
    const sorted = [...voices].sort((a, b) => (a.delay ?? 0) - (b.delay ?? 0));

    const pattern = [];
    let cursorMs = 0;

    for (const v of sorted) {
        const delayMs = (v.delay ?? 0) * 1000;
        const lengthMs = ((v.attack ?? 0.005) + (v.decay ?? 0.18)) * 1000;
        const energy = Math.min(1, Math.max(0, v.peak ?? 0.3));

        // sqrt, not linear: motor perception is closer to a power law than the gain is, and a
        // linear map makes every quiet upper voice vanish.
        const runMs = Math.max(8, Math.min(60, Math.sqrt(energy) * Math.min(lengthMs, 220) * 0.55));

        const gapMs = Math.max(0, delayMs - cursorMs);
        if (pattern.length > 0) {
            pattern.push(gapMs);
        } else if (gapMs > 0) {
            // A leading gap needs a zero-length "on" run in front of it, because the array
            // alternates starting with on.
            pattern.push(0, gapMs);
        }

        pattern.push(runMs);
        cursorMs = delayMs + runMs;
    }

    return pattern;
}

export const haptics = {
    init(audioEnabled = false) {
        state.supported = detectSupport();
        state.preference = readStoredPreference();
        state.audioEnabled = !!audioEnabled;
        return this.describe();
    },

    describe: () => ({
        supported: state.supported,
        // What is actually happening right now, which is the only thing a caller can act on.
        enabled: active(),
        // Whether the user pinned it, as opposed to inheriting the audio preference.
        explicit: state.preference !== null
    }),

    /** Called by fx.js whenever the audio preference changes, since haptics inherit from it. */
    syncAudio(enabled) {
        state.audioEnabled = !!enabled;
        return active();
    },

    /** null clears an explicit choice and returns to following audio. */
    setEnabled(enabled) {
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
        return active();
    },

    /** Stops anything currently running. Called on teardown and when haptics are turned off. */
    cancel() {
        try { navigator.vibrate?.(0); } catch { /* nothing to cancel */ }
    },

    // ── Cues. Each mirrors the audio voice of the same name. ──────────────────────────────

    /** Button press. Deliberately the shortest thing here — a tap should register, not announce. */
    tap() {
        if (throttled('tap', 40)) return false;
        return fire([10]);
    },

    /**
     * The resolution chord. Voices are added by score exactly as in audio.scoreLand, so a
     * bizarre restaurant is felt as a longer, more broken-up figure than a mundane one — the
     * strangeness is in the rhythm, which is the only place it can be.
     */
    scoreLand(score) {
        const strange = Math.min(1, Math.max(0, score / 100));
        const voices = [
            { delay: 0, attack: 0.01, decay: 0.9, peak: 0.5 },
            { delay: 0.03, attack: 0.01, decay: 0.85, peak: 0.36 },
            { delay: 0.06, attack: 0.01, decay: 0.8, peak: 0.30 }
        ];
        if (strange > 0.6) voices.push({ delay: 0.09, attack: 0.02, decay: 0.7, peak: 0.22 });
        if (strange > 0.85) voices.push({ delay: 0.12, attack: 0.02, decay: 0.65, peak: 0.18 });
        return fire(fromEnvelope(voices));
    },

    /** Ink hitting paper in three places. The offsets match audio.splat's noise bursts. */
    splat(intensity = 0.5) {
        if (throttled('splat', 200)) return false;
        const strength = Math.min(1, Math.max(0, intensity));
        return fire(fromEnvelope([
            { delay: 0, attack: 0.004, decay: 0.18 + strength * 0.2, peak: 0.16 + strength * 0.3 },
            { delay: 0.035, attack: 0.004, decay: 0.14, peak: 0.12 + strength * 0.2 },
            { delay: 0.06, attack: 0.004, decay: 0.12, peak: 0.10 + strength * 0.18 }
        ]));
    },

    /** One pipeline step. Light, because five of these land during a single wait. */
    phase() {
        if (throttled('phase', 120)) return false;
        return fire([12]);
    },

    /** Rising three-note share stinger — felt as an accelerating figure. */
    shareStinger() {
        if (throttled('share', 800)) return false;
        return fire(fromEnvelope([
            { delay: 0, attack: 0.004, decay: 0.14, peak: 0.34 },
            { delay: 0.08, attack: 0.004, decay: 0.14, peak: 0.32 },
            { delay: 0.16, attack: 0.004, decay: 0.30, peak: 0.34 }
        ]));
    },

    /**
     * Errors. The one cue with a deliberately different texture: two long, even runs. Every
     * other pattern here accelerates or decays, so a flat pair is unmistakable without being
     * louder — which is the same reason audio.error() is the only descending cue.
     */
    error() {
        if (throttled('error', 500)) return false;
        return fire([40, 60, 40]);
    },

    /** Something was added to a collection. A double tick — light, confirmatory, not a fanfare. */
    confirm() {
        if (throttled('confirm', 300)) return false;
        return fire([14, 46, 22]);
    },

    /**
     * A search leaving. Mirrors audio.locating: one run, a long gap, a much shorter second run.
     * The gap is the cue — a single buzz would be indistinguishable from the tap that caused it,
     * and what this has to say is that something is now outstanding.
     */
    locating() {
        if (throttled('locating', 700)) return false;
        return fire([18, 200, 8]);
    },

    /**
     * Results landing. The run count is the result count, on the same mapping audio.arrival
     * uses — so on a phone with the sound off, how much came back is still felt rather than
     * merely seen.
     */
    arrival(count = 0) {
        if (throttled('arrival', 600)) return false;
        const notes = Math.max(2, Math.min(5, Math.round(count / 4)));
        const voices = [];
        for (let i = 0; i < notes; i++) {
            voices.push({
                delay: i * 0.065,
                attack: 0.003,
                decay: i === notes - 1 ? 0.34 : 0.1,
                peak: 0.18 + i * 0.012
            });
        }
        return fire(fromEnvelope(voices));
    },

    /**
     * Tapping something that will cost a generation. One noticeably longer run than `tap`.
     * There is no matching cue for the cached case: that one is instant, and the absence of
     * extra weight is itself the signal.
     */
    tapUncached() {
        if (throttled('tap', 40)) return false;
        return fire([26]);
    },

    /** Raw escape hatch for a caller with its own shape. Clamped like everything else. */
    pulse: (pattern) => fire(Array.isArray(pattern) ? pattern : [pattern])
};

window.poseeHaptics = haptics;
