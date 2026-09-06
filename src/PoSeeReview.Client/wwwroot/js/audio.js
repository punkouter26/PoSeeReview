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
    master: null,      // Final trim. Everything meets here before the analyser.
    dry: null,         // Panned, unreverberated signal.
    reverbSend: null,  // Shared send bus into the convolver.
    convolver: null,
    wet: null,
    analyser: null,    // Tap for audio-reactive visuals; see analyse().
    analyserBins: null,
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

/**
 * A room, synthesised rather than downloaded. Exponentially decaying stereo noise is the
 * textbook cheap impulse response; the two channels are decorrelated (independent noise, and a
 * slightly different decay constant) because identical channels collapse the reverb to the
 * centre and undo the panning it is supposed to widen.
 *
 * Two early reflections are stamped in on top. Without them the tail reads as a hiss rather than
 * a space — the early part is what the ear uses to judge room size.
 */
function buildImpulseResponse(ctx, seconds = 1.3, decay = 3.2) {
    const length = Math.max(1, Math.floor(ctx.sampleRate * seconds));
    const impulse = ctx.createBuffer(2, length, ctx.sampleRate);

    for (let channel = 0; channel < 2; channel++) {
        const data = impulse.getChannelData(channel);
        const channelDecay = decay * (channel === 0 ? 1 : 1.07);
        for (let i = 0; i < length; i++) {
            const t = i / length;
            data[i] = (Math.random() * 2 - 1) * Math.pow(1 - t, channelDecay);
        }
        // Early reflections at ~11ms and ~23ms, offset per channel so they do not sum to mono.
        for (const [ms, amplitude] of [[11, 0.34], [23, 0.19]]) {
            const index = Math.floor(ctx.sampleRate * (ms + channel * 3) / 1000);
            if (index < length) {
                data[index] += amplitude * (channel === 0 ? 1 : -1);
            }
        }
    }
    return impulse;
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
        const ctx = state.ctx;

        state.master = ctx.createGain();
        // Headroom: several voices can overlap during the score reveal, and summing oscillators
        // at unity gain clips hard.
        state.master.gain.value = 0.22;

        // Analyser sits between the trim and the speakers, so what drives the visuals is exactly
        // what the user hears — including the reverb tail, which is most of the visible motion
        // after a transient.
        state.analyser = ctx.createAnalyser();
        state.analyser.fftSize = 256;
        state.analyser.smoothingTimeConstant = 0.72;
        state.analyserBins = new Uint8Array(state.analyser.frequencyBinCount);

        state.master.connect(state.analyser);
        state.analyser.connect(ctx.destination);

        // Dry path: every voice pans into here.
        state.dry = ctx.createGain();
        state.dry.gain.value = 1;
        state.dry.connect(state.master);

        // Wet path. The send is shared by every voice, so the room is built once rather than
        // per note — a convolver per voice would be hundreds of convolutions during a count-up.
        state.reverbSend = ctx.createGain();
        state.reverbSend.gain.value = 1;

        state.convolver = ctx.createConvolver();
        state.convolver.normalize = true;
        state.convolver.buffer = buildImpulseResponse(ctx);

        state.wet = ctx.createGain();
        state.wet.gain.value = 0.30;

        state.reverbSend.connect(state.convolver);
        state.convolver.connect(state.wet);
        state.wet.connect(state.master);
    } catch {
        state.ctx = null;
    }

    return state.ctx;
}

/**
 * Per-voice output stage: pan, then split to the dry bus and the shared reverb send.
 * StereoPannerNode rather than PannerNode — this is a flat 2D interface, so an HRTF panner would
 * spend real CPU modelling a head for positions that only ever vary along one axis.
 */
function makeOutput(pan = 0, send = 0.25) {
    const ctx = state.ctx;
    const panner = ctx.createStereoPanner
        ? ctx.createStereoPanner()
        : null;

    const head = panner ?? ctx.createGain();
    if (panner) {
        panner.pan.value = Math.max(-1, Math.min(1, pan));
    }

    head.connect(state.dry);

    let sendGain = null;
    if (send > 0) {
        sendGain = ctx.createGain();
        sendGain.gain.value = send;
        head.connect(sendGain);
        sendGain.connect(state.reverbSend);
    }

    return {
        node: head,
        dispose() {
            try { head.disconnect(); } catch { /* already torn down */ }
            if (sendGain) {
                try { sendGain.disconnect(); } catch { /* already torn down */ }
            }
        }
    };
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
function voice({ type = 'sine', freq, startFreq, endFreq, attack = 0.005, decay = 0.18, peak = 1, delay = 0, detune = 0, pan = 0, send = 0.25 }) {
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

    const output = makeOutput(pan, send);
    osc.connect(gain);
    gain.connect(output.node);

    osc.start(t0);
    osc.stop(t0 + attack + decay + 0.02);
    osc.onended = () => {
        // Explicit teardown: WebAudio nodes are not collected while connected, and this app can
        // fire hundreds of these during a single score reveal. The panner and send are part of
        // that chain now, so they have to be released here too or the graph grows unboundedly.
        osc.disconnect();
        gain.disconnect();
        output.dispose();
    };
}

/** Short filtered-noise burst — used for texture (paper, ink, impact) rather than pitch. */
function noise({ duration = 0.12, peak = 0.5, filterHz = 1800, filterType = 'lowpass', delay = 0, pan = 0, send = 0.35 }) {
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

    const output = makeOutput(pan, send);
    source.connect(filter);
    filter.connect(gain);
    gain.connect(output.node);

    source.start(t0);
    source.onended = () => {
        source.disconnect();
        filter.disconnect();
        gain.disconnect();
        output.dispose();
    };
}

/**
 * Maps a screen x coordinate to a stereo position. Clamped to ±0.85 rather than ±1: a sound
 * hard-panned to one channel disappears entirely on a phone held with one speaker covered, and
 * on headphones it sits outside the head rather than in the scene.
 */
function panForClientX(clientX) {
    const width = window.innerWidth || 1;
    const normalised = (clientX / width) * 2 - 1;
    return Math.max(-0.85, Math.min(0.85, normalised));
}

/** Stereo position of an element's centre. Returns 0 for anything unmeasurable. */
function panForElement(element) {
    try {
        const rect = element?.getBoundingClientRect?.();
        if (!rect || rect.width === 0) return 0;
        return panForClientX(rect.left + rect.width / 2);
    } catch {
        return 0;
    }
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

    /**
     * Soft click for buttons and card taps. Pans to wherever the control actually is: a tap on
     * a card in the right-hand column of the discovery grid clicks from the right. This is the
     * cheapest spatial cue in the app and the one users notice without being able to name.
     */
    tap(element = null) {
        if (!canPlay() || throttled('tap', 40)) return;
        voice({
            type: 'triangle', freq: 660, attack: 0.002, decay: 0.05, peak: 0.35,
            pan: element ? panForElement(element) : 0,
            send: 0.14   // A UI click in a big reverb sounds like a mistake, not a room.
        });
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
            peak: 0.10 + progress * 0.06,
            // The count-up sweeps left to right as it climbs, so the reveal has direction as
            // well as pitch. Narrow (±0.5) — a full-width sweep on a ticking counter is seasick.
            pan: (progress * 2 - 1) * 0.5,
            send: 0.18
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

        // The chord is spread across the stereo field the way an ensemble is: root centred and
        // anchoring, upper voices to either side. Mono-stacked sines at these intervals beat
        // against each other in one spot and sound like a synth patch; spread, they sound wide.
        // The reverb send rises with strangeness, so a bizarre score resolves into a bigger,
        // stranger room than a mundane one.
        const room = 0.22 + strange * 0.45;

        voice({ type: 'sine', freq: root, attack: 0.01, decay: 0.9, peak: 0.5, pan: 0, send: room });
        voice({ type: 'sine', freq: root * 1.5, attack: 0.01, decay: 0.85, peak: 0.36, delay: 0.03, pan: -0.45, send: room });
        voice({ type: 'sine', freq: root * 2, attack: 0.01, decay: 0.8, peak: 0.30, delay: 0.06, pan: 0.45, send: room });

        if (strange > 0.6) {
            // Major seventh — bright but unresolved.
            voice({ type: 'sine', freq: root * 1.888, attack: 0.02, decay: 0.7, peak: 0.22, delay: 0.09, pan: -0.7, send: room });
        }
        if (strange > 0.85) {
            // Tritone. Deliberately uncomfortable; only the genuinely bizarre earns it. Panned
            // opposite the seventh so the two dissonances pull the image apart rather than
            // fighting in the centre.
            voice({ type: 'sine', freq: root * 1.414, attack: 0.02, decay: 0.65, peak: 0.18, delay: 0.12, pan: 0.7, send: room });
        }

        noise({
            duration: 0.25, peak: 0.10, filterHz: 900 + strange * 2600,
            filterType: 'lowpass', delay: 0.02, send: room
        });
    },

    /**
     * One step of the generation pipeline completed. Rising scale degree per phase, so five
     * phases sound like ascent rather than five identical beeps.
     */
    phase(index, total) {
        if (!canPlay() || throttled('phase', 120)) return;

        const clamped = Math.min(Math.max(index, 0), Math.max(0, total - 1));
        const note = PENTATONIC[clamped % PENTATONIC.length];

        // Phases sweep left to right across the pipeline, matching the stepper the user is
        // watching. With a single phase there is nowhere to sweep to, so it stays centred.
        const span = Math.max(1, total - 1);
        const pan = total > 1 ? ((clamped / span) * 2 - 1) * 0.65 : 0;

        voice({ type: 'triangle', freq: note * 0.5, attack: 0.006, decay: 0.22, peak: 0.30, pan, send: 0.3 });
        voice({ type: 'sine', freq: note, attack: 0.006, decay: 0.16, peak: 0.16, delay: 0.02, pan, send: 0.3 });
    },

    /**
     * Ink-splatter texture to accompany the particle burst. Three noise bursts at spread
     * positions rather than one in the middle: ink thrown at a page lands in several places, and
     * the particles the user is watching are spread across the whole canvas.
     */
    splat(intensity = 0.5) {
        if (!canPlay() || throttled('splat', 200)) return;
        const strength = Math.min(1, Math.max(0, intensity));

        for (const [pan, delay, scale] of [[0, 0, 1], [-0.62, 0.035, 0.7], [0.55, 0.06, 0.6]]) {
            noise({
                duration: (0.18 + strength * 0.2) * scale,
                peak: (0.16 + strength * 0.16) * scale,
                filterHz: 500 + strength * 1500,
                delay,
                pan,
                send: 0.3 + strength * 0.25
            });
        }
        // The body thump stays centred — a panned sub just sounds like a broken speaker.
        voice({ type: 'sine', startFreq: 180, endFreq: 40, attack: 0.004, decay: 0.24, peak: 0.28, pan: 0, send: 0.1 });
    },

    /** Short rising stinger on a completed share. Rises in pitch and travels left to right. */
    shareStinger() {
        if (!canPlay() || throttled('share', 800)) return;
        voice({ type: 'triangle', freq: 523.25, attack: 0.004, decay: 0.14, peak: 0.34, pan: -0.5, send: 0.28 });
        voice({ type: 'triangle', freq: 659.25, attack: 0.004, decay: 0.14, peak: 0.32, delay: 0.08, pan: 0, send: 0.3 });
        voice({ type: 'triangle', freq: 783.99, attack: 0.004, decay: 0.30, peak: 0.34, delay: 0.16, pan: 0.5, send: 0.36 });
        noise({ duration: 0.3, peak: 0.06, filterHz: 4200, filterType: 'highpass', delay: 0.16, pan: 0.3, send: 0.4 });
    },

    /**
     * Descending pair for errors — distinct from every rising cue above. Deliberately dry and
     * centred: an error that echoes around a large room reads as ambience, not as a problem.
     */
    error() {
        if (!canPlay() || throttled('error', 500)) return;
        voice({ type: 'sawtooth', freq: 311.13, attack: 0.006, decay: 0.18, peak: 0.22, pan: 0, send: 0.05 });
        voice({ type: 'sawtooth', freq: 233.08, attack: 0.006, decay: 0.34, peak: 0.24, delay: 0.11, pan: 0, send: 0.05 });
    },

    /** Stereo position for a DOM element, exposed so callers can pan a sound to a control. */
    panForElement,

    /**
     * Click panned to a raw viewport x coordinate. This is the form Blazor call sites can
     * actually use: MouseEventArgs already carries ClientX, whereas panning by element would
     * mean capturing an @ref on every button in a repeated list.
     */
    tapAt(clientX) {
        if (!canPlay() || throttled('tap', 40)) return;
        voice({
            type: 'triangle', freq: 660, attack: 0.002, decay: 0.05, peak: 0.35,
            pan: panForClientX(clientX), send: 0.14
        });
    },

    /**
     * Spectrum snapshot for audio-reactive visuals: overall level plus three bands. Returns
     * silence when nothing is playing, so a caller can drive a shader uniform unconditionally
     * without branching on whether sound is even enabled.
     */
    analyse() {
        if (!state.analyser || !canPlay()) {
            return { level: 0, bass: 0, mid: 0, treble: 0 };
        }

        try {
            state.analyser.getByteFrequencyData(state.analyserBins);
        } catch {
            return { level: 0, bass: 0, mid: 0, treble: 0 };
        }

        const bins = state.analyserBins;
        const count = bins.length;
        // Band edges as fractions of the bin range. The FFT is linear in frequency and hearing
        // is not, so "bass" is a small slice of bins and "treble" a large one.
        const bassEnd = Math.max(1, Math.floor(count * 0.08));
        const midEnd = Math.max(bassEnd + 1, Math.floor(count * 0.35));

        let bass = 0, mid = 0, treble = 0, total = 0;
        for (let i = 0; i < count; i++) {
            const value = bins[i] / 255;
            total += value;
            if (i < bassEnd) bass += value;
            else if (i < midEnd) mid += value;
            else treble += value;
        }

        return {
            level: total / count,
            bass: bass / bassEnd,
            mid: mid / (midEnd - bassEnd),
            treble: treble / Math.max(1, count - midEnd)
        };
    },

    /** Output latency, for the diagnostics overlay. Null when there is no context yet. */
    latency() {
        if (!state.ctx) return null;
        return {
            baseMs: (state.ctx.baseLatency ?? 0) * 1000,
            outputMs: (state.ctx.outputLatency ?? 0) * 1000,
            sampleRate: state.ctx.sampleRate,
            contextState: state.ctx.state
        };
    },

    dispose() {
        if (state.ctx) {
            try { state.ctx.close(); } catch { /* already closing */ }
        }
        state.ctx = null;
        state.master = null;
        state.dry = null;
        state.reverbSend = null;
        state.convolver = null;
        state.wet = null;
        state.analyser = null;
        state.analyserBins = null;
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
