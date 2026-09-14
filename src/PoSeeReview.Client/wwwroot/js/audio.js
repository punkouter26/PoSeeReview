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

    // Tells the ambient bed to get out of the way. Fired on schedule rather than on start, so a
    // delayed voice in a chord ducks when it sounds rather than when it was queued.
    notifyVoice(peak);

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

    notifyVoice(peak);

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

// ── Seeded generation, for the per-comic signature ───────────────────────────────────────

/**
 * FNV-1a over a string. Any stable hash would do; this one is four lines and has no collisions
 * that matter at the scale of "one motif per restaurant".
 */
function hashString(text) {
    let hash = 0x811c9dc5;
    const value = String(text ?? '');
    for (let i = 0; i < value.length; i++) {
        hash ^= value.charCodeAt(i);
        hash = Math.imul(hash, 0x01000193);
    }
    return hash >>> 0;
}

/** mulberry32 — a small, fast, well-distributed PRNG. Deterministic from its seed, which is the point. */
function seededRandom(seed) {
    let a = seed >>> 0;
    return () => {
        a = (a + 0x6d2b79f5) >>> 0;
        let t = a;
        t = Math.imul(t ^ (t >>> 15), t | 1);
        t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
        return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
    };
}

/**
 * Scale degrees in semitones, indexed by how strange the comic is. A place scoring 20 gets a
 * major pentatonic; one scoring 95 gets an octatonic set full of tritones. The scale is the
 * single biggest carrier of "how weird is this" — bigger than tempo, bigger than timbre — which
 * is why it is chosen by score rather than by the seed.
 */
const SCALES = [
    [0, 2, 4, 7, 9],           // major pentatonic — mundane
    [0, 2, 3, 7, 9],           // minor pentatonic — a bit off
    [0, 2, 3, 5, 7, 8, 10],    // natural minor — unsettled
    [0, 1, 3, 4, 6, 7, 9, 10], // octatonic — genuinely strange
    [0, 1, 4, 6, 7, 10]        // no name worth having — reserved for the top band
];

const SEMITONE = Math.pow(2, 1 / 12);

/** Listeners fired whenever a voice starts, so the ambient bed can duck under it. */
const voiceListeners = new Set();

function notifyVoice(peak) {
    if (voiceListeners.size === 0) return;
    for (const listener of voiceListeners) {
        try {
            listener(peak);
        } catch {
            // A misbehaving listener must not stop a sound from playing.
        }
    }
}

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
        } else {
            // Narration is a separate output that does not pass through the context, so
            // suspending the graph would leave a voice mid-sentence talking over a muted app.
            this.stopNarration();

            if (state.ctx && state.ctx.state === 'running') {
                try {
                    await state.ctx.suspend();
                } catch {
                    // Suspension is best-effort; nothing further will be scheduled anyway.
                }
                state.unlocked = false;
            }
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

    /** Confirmation for something added to a collection. Two rising ticks, deliberately small. */
    confirm() {
        if (!canPlay() || throttled('confirm', 300)) return;
        voice({ type: 'sine', freq: 783.99, attack: 0.003, decay: 0.09, peak: 0.26, pan: -0.2, send: 0.2 });
        voice({ type: 'sine', freq: 1046.5, attack: 0.003, decay: 0.16, peak: 0.22, delay: 0.07, pan: 0.2, send: 0.26 });
    },

    /**
     * One comic panel finishing its reveal. Distinct from `phase`, which narrates the pipeline:
     * this is the sound of a panel appearing, so it is short, dry and panned to where the panel
     * actually is in the strip.
     */
    panelReveal(index, total) {
        if (!canPlay() || throttled(`panel${index}`, 90)) return;

        const span = Math.max(1, total - 1);
        const pan = total > 1 ? ((index / span) * 2 - 1) * 0.7 : 0;

        // A quick swept blip, like ink hitting paper and stopping. The sweep is downward so four
        // of them in sequence do not read as an ascending scale — the panels are siblings, not
        // steps, and an ascending figure would imply the last one matters most.
        voice({
            type: 'triangle', startFreq: 880 + index * 40, endFreq: 420,
            attack: 0.002, decay: 0.13, peak: 0.20, pan, send: 0.16
        });
        noise({ duration: 0.09, peak: 0.10, filterHz: 2400, delay: 0.005, pan, send: 0.18 });
    },

    /**
     * Moderation outcome, graded by severity. Hide is reversible and sounds like it; suppress is
     * a door closing; remove is the only cue in the app that ends below where it started and
     * does not resolve. A moderator running a queue hears which action they took without
     * reading the confirmation, which is the point — the three are one mis-tap apart.
     */
    severity(level) {
        if (!canPlay() || throttled('severity', 250)) return;

        switch (level) {
            case 'remove':
                voice({ type: 'sawtooth', freq: 174.61, attack: 0.008, decay: 0.5, peak: 0.26, pan: 0, send: 0.08 });
                voice({ type: 'sine', startFreq: 130, endFreq: 46, attack: 0.006, decay: 0.7, peak: 0.30, delay: 0.06, pan: 0, send: 0.1 });
                noise({ duration: 0.4, peak: 0.10, filterHz: 380, delay: 0.02, send: 0.12 });
                break;

            case 'suppress':
                voice({ type: 'square', freq: 261.63, attack: 0.005, decay: 0.22, peak: 0.20, pan: -0.25, send: 0.12 });
                voice({ type: 'square', freq: 196.00, attack: 0.005, decay: 0.34, peak: 0.22, delay: 0.09, pan: 0.25, send: 0.12 });
                break;

            default: // hide — reversible, so it stays light and does not descend.
                voice({ type: 'triangle', freq: 440, attack: 0.004, decay: 0.14, peak: 0.20, pan: 0, send: 0.18 });
                break;
        }
    },

    /**
     * A comic's own motif, derived from its place id and its score.
     *
     * WHY THIS IS NOT DECORATION. Every restaurant now sounds like itself, deterministically and
     * forever: the same place always plays the same figure, so the Hall of Fame becomes something
     * you can recognise by ear. Nothing else in the app gives a place an identity that is not its
     * name.
     *
     * The seed picks the notes and the contour; the SCORE picks the scale, the tempo and the
     * timbre — so two restaurants sound different from each other, and a strange one sounds
     * strange rather than merely different. Zero assets: it is the same `voice()` primitive
     * everything else uses, driven by a seeded PRNG.
     */
    signature(seed, score = 50) {
        if (!canPlay() || throttled('signature', 600)) return;

        const strange = Math.min(1, Math.max(0, score / 100));
        const random = seededRandom(hashString(seed));

        const scale = SCALES[Math.min(SCALES.length - 1, Math.floor(strange * SCALES.length))];

        // Root within a fifth of A3, chosen by the seed. Keeping every motif in one octave means
        // two places played back to back are comparable rather than merely different in register.
        const rootSemitone = Math.floor(random() * 8) - 3;
        const root = 220 * Math.pow(SEMITONE, rootSemitone);

        // Faster as it gets stranger. 96bpm at zero, ~184 at 100.
        const beat = 60 / (96 + strange * 88) / 2;

        // Timbre tracks strangeness too: a sine is inoffensive, a sawtooth is not.
        const type = strange < 0.33 ? 'sine' : strange < 0.66 ? 'triangle' : 'sawtooth';

        const noteCount = 4;
        for (let i = 0; i < noteCount; i++) {
            const degree = Math.floor(random() * scale.length);
            // The last note jumps an octave a third of the time — a motif that ends where it sat
            // is a scale fragment, not a phrase.
            const octave = (i === noteCount - 1 && random() < 0.34) ? 12 : 0;
            const freq = root * Math.pow(SEMITONE, scale[degree] + octave);

            voice({
                type,
                freq,
                attack: 0.006,
                decay: beat * (i === noteCount - 1 ? 3.2 : 1.5),
                peak: 0.20 - i * 0.015,
                delay: i * beat,
                // Slight detune that grows with strangeness: at the top of the range the motif is
                // audibly out of tune with itself.
                detune: (random() - 0.5) * strange * 34,
                pan: (random() - 0.5) * 1.2,
                send: 0.2 + strange * 0.4
            });
        }
    },

    /**
     * Plays a numeric series as pitch — the shape of a chart, heard.
     *
     * The Insights page draws four charts over every score the app has recorded and spends
     * nothing to do it. This spends nothing either, and it answers a question a static chart
     * cannot: whether a distribution is flat, humped or bimodal is instantly obvious as a
     * contour, including to someone who cannot see the SVG at all.
     *
     * Long series are DECIMATED, not truncated. Firing an oscillator per row of a 500-point
     * series would be 500 nodes and several seconds of noise; picking evenly across the whole
     * range preserves the shape, which is the only thing being communicated.
     */
    sonify(values, options = {}) {
        if (!canPlay() || !Array.isArray(values) || values.length === 0) return;
        if (throttled('sonify', 400)) return;

        const maxNotes = Math.min(options.maxNotes ?? 32, 48);
        const stride = Math.max(1, Math.ceil(values.length / maxNotes));

        const picked = [];
        for (let i = 0; i < values.length; i += stride) {
            const value = Number(values[i]);
            if (Number.isFinite(value)) picked.push(value);
        }
        if (picked.length === 0) return;

        const min = options.min ?? Math.min(...picked);
        const max = options.max ?? Math.max(...picked);
        const span = max - min;

        const totalMs = Math.min(options.durationMs ?? 1800, 4000);
        const step = (totalMs / 1000) / picked.length;

        picked.forEach((value, i) => {
            // A flat series maps everything to the middle of the range rather than dividing by
            // zero — and a flat line SHOULD sound flat.
            const normalised = span > 0 ? (value - min) / span : 0.5;

            // Two octaves of pentatonic, so an arbitrary series is always consonant with itself.
            const index = Math.min(PENTATONIC.length * 2 - 1,
                Math.floor(normalised * PENTATONIC.length * 2));
            const freq = PENTATONIC[index % PENTATONIC.length]
                * (index >= PENTATONIC.length ? 2 : 1)
                * 0.5;

            voice({
                type: 'sine',
                freq,
                attack: 0.004,
                decay: Math.max(0.08, step * 1.6),
                peak: 0.13,
                delay: i * step,
                // Sweeps left to right across the series, so position in the sequence is audible
                // as well as position in the pitch range. Without it a hump and a dip are the
                // same set of notes in a different order, which the ear does not reliably parse.
                pan: ((i / Math.max(1, picked.length - 1)) * 2 - 1) * 0.75,
                send: 0.22
            });
        });
    },

    // ── Narration ────────────────────────────────────────────────────────────────────────
    //
    // speechSynthesis is a separate output from the AudioContext — it does not pass through the
    // graph, the analyser or the master trim, and it works even before unlock. So it is gated on
    // the enabled preference alone, and it is cancelled rather than mixed: two voices reading
    // over each other is worse than either.

    canNarrate() {
        try {
            return typeof window.speechSynthesis !== 'undefined'
                && typeof window.SpeechSynthesisUtterance === 'function';
        } catch {
            return false;
        }
    },

    /**
     * Reads the comic's narrative aloud. An accessibility win before it is a flourish: the
     * narrative is the joke, and it currently exists only as text under an image that a screen
     * reader describes as a comic strip.
     */
    narrate(text, options = {}) {
        if (!state.enabled || !this.canNarrate()) return false;

        const value = String(text ?? '').trim();
        if (!value) return false;

        try {
            // Always cancel first. Chrome queues utterances indefinitely, so a user tapping
            // narrate twice would otherwise hear the whole thing twice, back to back.
            window.speechSynthesis.cancel();

            const utterance = new SpeechSynthesisUtterance(value.slice(0, 800));
            utterance.rate = options.rate ?? 1.02;
            utterance.pitch = options.pitch ?? 1.0;
            utterance.volume = options.volume ?? 0.9;
            window.speechSynthesis.speak(utterance);
            return true;
        } catch {
            return false;
        }
    },

    stopNarration() {
        try { window.speechSynthesis?.cancel(); } catch { /* nothing speaking */ }
    },

    // ── Hooks for composed modules ───────────────────────────────────────────────────────

    /** The live AudioContext, or null before unlock. Used by ambient.js to host its worklet. */
    context: () => state.ctx,

    /**
     * A panned, reverb-sent output stage, for a module that generates its own signal. This is the
     * same stage every built-in voice uses, exposed so the ambient bed sits in the same room as
     * everything else rather than beside it.
     */
    createBus(options = {}) {
        if (!state.ctx) return null;
        return makeOutput(options.pan ?? 0, options.send ?? 0.25);
    },

    /**
     * Fires whenever a voice is scheduled, with its peak gain. The ambient bed subscribes so it
     * can duck; the coupling points one way, exactly as audio-reactive.js does for the gradient.
     */
    onVoice(listener) {
        voiceListeners.add(listener);
        return () => voiceListeners.delete(listener);
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
