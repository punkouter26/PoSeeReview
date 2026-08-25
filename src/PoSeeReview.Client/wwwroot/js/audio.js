// Programmatic sound. No sample files, no asset fetches — every sound here is oscillators and
// filtered noise built at call time, so the whole audible identity of the app costs a few KB of
// source and zero network bytes.
//
// Two rules shape the design:
//
//  1. An AudioContext created before a user gesture starts 'suspended' and never recovers on
//     its own. So the context is created lazily on the first real interaction, and every play
//     call is a no-op until then rather than an error.
//  2. Sound is opt-out-able and must default to quiet. Audio that a user did not ask for, on a
//     page they opened in a shared space, is a hostile surprise.

import { gfx } from './gfx-core.js';

const STORAGE_KEY = 'posee_audio_enabled';

const state = {
    ctx: null,
    master: null,
    enabled: false,
    unlocked: false,
    // Guards against a burst of identical sounds (a fast count-up) stacking into clipping.
    lastPlayedAt: new Map()
};

function readStoredEnabled() {
    try {
        return localStorage.getItem(STORAGE_KEY) === 'true';
    } catch {
        return false;
    }
}

function ensureContext() {
    if (state.ctx) {
        return state.ctx;
    }

    const Ctor = window.AudioContext || window.webkitAudioContext;
    if (!Ctor) {
        return null;
    }

    try {
        state.ctx = new Ctor({ latencyHint: 'interactive' });
        state.master = state.ctx.createGain();
        // Headroom: several voices can overlap during the score reveal, and summing oscillators
        // at unity gain clips hard.
        state.master.gain.value = 0.22;
        state.master.connect(state.ctx.destination);
    } catch {
        state.ctx = null;
    }

    return state.ctx;
}

function canPlay() {
    return state.enabled && state.unlocked && state.ctx && state.ctx.state === 'running';
}

/** Rate-limits one sound key so rapid repeats thin out instead of piling up. */
function throttled(key, minGapMs) {
    const now = performance.now();
    const last = state.lastPlayedAt.get(key) ?? -Infinity;
    if (now - last < minGapMs) {
        return true;
    }
    state.lastPlayedAt.set(key, now);
    return false;
}

/**
 * One synth voice: an oscillator through its own gain envelope.
 * The envelope always ends at an explicit zero — a note left at a non-zero gain keeps its
 * oscillator alive and audible forever.
 */
function voice({ type = 'sine', freq, startFreq, endFreq, attack = 0.005, decay = 0.18, peak = 1, delay = 0, detune = 0 }) {
    const ctx = state.ctx;
    const t0 = ctx.currentTime + delay;

    const osc = ctx.createOscillator();
    osc.type = type;
    osc.detune.value = detune;

    if (startFreq && endFreq) {
        osc.frequency.setValueAtTime(startFreq, t0);
        osc.frequency.exponentialRampToValueAtTime(Math.max(1, endFreq), t0 + attack + decay);
    } else {
        osc.frequency.setValueAtTime(freq, t0);
    }

    const gain = ctx.createGain();
    gain.gain.setValueAtTime(0.0001, t0);
    gain.gain.exponentialRampToValueAtTime(Math.max(0.0001, peak), t0 + attack);
    gain.gain.exponentialRampToValueAtTime(0.0001, t0 + attack + decay);

    osc.connect(gain);
    gain.connect(state.master);

    osc.start(t0);
    osc.stop(t0 + attack + decay + 0.02);
    osc.onended = () => {
        // Explicit teardown: WebAudio nodes are not collected while connected, and this app can
        // fire hundreds of these during a single score reveal.
        osc.disconnect();
        gain.disconnect();
    };
}

/** Short filtered-noise burst — used for texture (paper, ink, impact) rather than pitch. */
function noise({ duration = 0.12, peak = 0.5, filterHz = 1800, filterType = 'lowpass', delay = 0 }) {
    const ctx = state.ctx;
    const t0 = ctx.currentTime + delay;
    const frameCount = Math.max(1, Math.floor(ctx.sampleRate * duration));

    const buffer = ctx.createBuffer(1, frameCount, ctx.sampleRate);
    const data = buffer.getChannelData(0);
    for (let i = 0; i < frameCount; i++) {
        data[i] = Math.random() * 2 - 1;
    }

    const source = ctx.createBufferSource();
    source.buffer = buffer;

    const filter = ctx.createBiquadFilter();
    filter.type = filterType;
    filter.frequency.value = filterHz;

    const gain = ctx.createGain();
    gain.gain.setValueAtTime(Math.max(0.0001, peak), t0);
    gain.gain.exponentialRampToValueAtTime(0.0001, t0 + duration);

    source.connect(filter);
    filter.connect(gain);
    gain.connect(state.master);

    source.start(t0);
    source.onended = () => {
        source.disconnect();
        filter.disconnect();
        gain.disconnect();
    };
}

// A pentatonic set, so any combination of these is consonant. The score count-up plays notes in
// effectively random order; on a diatonic scale that produces semitone clashes.
const PENTATONIC = [523.25, 587.33, 698.46, 783.99, 932.33]; // C5 D5 F5 G5 A#5

export const audio = {
    /** Reads the stored preference. Does NOT create a context — that needs a user gesture. */
    init() {
        state.enabled = readStoredEnabled();
        return { enabled: state.enabled, unlocked: state.unlocked };
    },

    isEnabled: () => state.enabled,

    /**
     * Must be called from inside a real user-gesture handler. Browsers only allow an
     * AudioContext to enter 'running' from a trusted event; calling this from a timer or an
     * await continuation silently leaves it suspended.
     */
    async unlock() {
        if (!state.enabled) {
            return false;
        }

        const ctx = ensureContext();
        if (!ctx) {
            return false;
        }

        try {
            if (ctx.state === 'suspended') {
                await ctx.resume();
            }
            state.unlocked = ctx.state === 'running';
        } catch {
            state.unlocked = false;
        }

        return state.unlocked;
    },

    async setEnabled(enabled) {
        state.enabled = !!enabled;
        try {
            localStorage.setItem(STORAGE_KEY, String(state.enabled));
        } catch {
            // Session-only preference.
        }

        if (state.enabled) {
            await this.unlock();
        } else if (state.ctx && state.ctx.state === 'running') {
            try {
                await state.ctx.suspend();
            } catch {
                // Suspension is best-effort; nothing further will be scheduled anyway.
            }
            state.unlocked = false;
        }

        return state.enabled;
    },

    /** Soft click for buttons and card taps. */
    tap() {
        if (!canPlay() || throttled('tap', 40)) return;
        voice({ type: 'triangle', freq: 660, attack: 0.002, decay: 0.05, peak: 0.35 });
    },

    /**
     * One tick of the strangeness count-up. Pitch rises with the score so the reveal audibly
     * climbs; the note is picked from a pentatonic set so ticks never clash.
     */
    scoreTick(value, target) {
        if (!canPlay() || throttled('tick', 22)) return;

        const progress = target > 0 ? Math.min(1, value / target) : 0;
        const note = PENTATONIC[Math.min(PENTATONIC.length - 1, Math.floor(progress * PENTATONIC.length))];
        voice({
            type: 'square',
            freq: note,
            attack: 0.001,
            decay: 0.045,
            // Ticks fade back as the count rises so the final chord is the loudest thing.
            peak: 0.10 + progress * 0.06
        });
    },

    /**
     * Resolution chord when the count lands. Voiced by score: a low strangeness resolves to a
     * plain major triad, a high one adds a major seventh and a tritone above for unease.
     */
    scoreLand(score) {
        if (!canPlay()) return;

        const root = 261.63; // C4
        const strange = Math.min(1, Math.max(0, score / 100));

        voice({ type: 'sine', freq: root, attack: 0.01, decay: 0.9, peak: 0.5 });
        voice({ type: 'sine', freq: root * 1.5, attack: 0.01, decay: 0.85, peak: 0.36, delay: 0.03 });
        voice({ type: 'sine', freq: root * 2, attack: 0.01, decay: 0.8, peak: 0.30, delay: 0.06 });

        if (strange > 0.6) {
            // Major seventh — bright but unresolved.
            voice({ type: 'sine', freq: root * 1.888, attack: 0.02, decay: 0.7, peak: 0.22, delay: 0.09 });
        }
        if (strange > 0.85) {
            // Tritone. Deliberately uncomfortable; only the genuinely bizarre earns it.
            voice({ type: 'sine', freq: root * 1.414, attack: 0.02, decay: 0.65, peak: 0.18, delay: 0.12 });
        }

        noise({ duration: 0.25, peak: 0.10, filterHz: 900 + strange * 2600, filterType: 'lowpass', delay: 0.02 });
    },

    /**
     * One step of the generation pipeline completed. Rising scale degree per phase, so five
     * phases sound like ascent rather than five identical beeps.
     */
    phase(index, total) {
        if (!canPlay() || throttled('phase', 120)) return;

        const clamped = Math.min(Math.max(index, 0), Math.max(0, total - 1));
        const note = PENTATONIC[clamped % PENTATONIC.length];
        voice({ type: 'triangle', freq: note * 0.5, attack: 0.006, decay: 0.22, peak: 0.30 });
        voice({ type: 'sine', freq: note, attack: 0.006, decay: 0.16, peak: 0.16, delay: 0.02 });
    },

    /** Ink-splatter texture to accompany the particle burst. */
    splat(intensity = 0.5) {
        if (!canPlay() || throttled('splat', 200)) return;
        const strength = Math.min(1, Math.max(0, intensity));
        noise({ duration: 0.18 + strength * 0.2, peak: 0.16 + strength * 0.16, filterHz: 500 + strength * 1500 });
        voice({ type: 'sine', startFreq: 180, endFreq: 40, attack: 0.004, decay: 0.24, peak: 0.28 });
    },

    /** Short rising stinger on a completed share. */
    shareStinger() {
        if (!canPlay() || throttled('share', 800)) return;
        voice({ type: 'triangle', freq: 523.25, attack: 0.004, decay: 0.14, peak: 0.34 });
        voice({ type: 'triangle', freq: 659.25, attack: 0.004, decay: 0.14, peak: 0.32, delay: 0.08 });
        voice({ type: 'triangle', freq: 783.99, attack: 0.004, decay: 0.30, peak: 0.34, delay: 0.16 });
        noise({ duration: 0.3, peak: 0.06, filterHz: 4200, filterType: 'highpass', delay: 0.16 });
    },

    /** Descending pair for errors — distinct from every rising cue above. */
    error() {
        if (!canPlay() || throttled('error', 500)) return;
        voice({ type: 'sawtooth', freq: 311.13, attack: 0.006, decay: 0.18, peak: 0.22 });
        voice({ type: 'sawtooth', freq: 233.08, attack: 0.006, decay: 0.34, peak: 0.24, delay: 0.11 });
    },

    dispose() {
        if (state.ctx) {
            try { state.ctx.close(); } catch { /* already closing */ }
        }
        state.ctx = null;
        state.master = null;
        state.unlocked = false;
        state.lastPlayedAt.clear();
    }
};

// Audio is independent of the GPU tier by design: it stays available at 'lite' and 'off',
// because a device that cannot run shaders can still comfortably run four oscillators. The one
// exception is an explicit reduced-motion request, which we read as "no unrequested stimulus".
gfx.onTierChanged(() => {
    if (gfx.describe().reducedMotion && state.enabled) {
        audio.setEnabled(false);
    }
});

window.poseeAudio = audio;
