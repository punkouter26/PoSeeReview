// AudioWorkletProcessor for the ambient bed. Runs on the audio thread.
//
// WHY A WORKLET AND NOT MORE OSCILLATOR NODES.
//
// Every other sound in this app is a transient: a tap, a tick, a chord that decays in under a
// second. For those, OscillatorNode is exactly right — the envelope is scheduled against
// ctx.currentTime, which is already sample-accurate, and the node tears itself down on ended.
//
// A continuous bed is the opposite case. It has to exist for as long as the page does, drift for
// that whole time, and never repeat audibly. Built from nodes that is a dozen oscillators, a
// dozen gains and a bank of LFOs, all of them alive forever and all of their parameter changes
// scheduled from the main thread — the same thread that is running Blazor renders, the shared
// rAF loop and garbage collection. Nothing about that is sample-accurate, and the audible result
// of a late parameter write on a sustained tone is a click.
//
// Here the whole bed is one node, generated per sample on the audio thread, and it cannot be
// late because there is no main thread in the path at all. The main thread's only job is to post
// a target now and then, which this smooths toward.
//
// NO IMPORTS. A worklet module is evaluated in the AudioWorkletGlobalScope, which has no DOM, no
// window, no fetch, and no module graph shared with the page. This file must stand alone.
//
// THE PARAMETERS ARE TARGETS, NOT VALUES. Every one is approached with a one-pole smoother, so a
// score changing from 12 to 97 glides rather than jumping. A jump on a sustained tone is a click,
// and a click is the one sound a background bed must never make.

const TWO_PI = Math.PI * 2;

/** Time constant for parameter smoothing, in seconds. Slow enough to be inaudible as a move. */
const GLIDE_SECONDS = 0.9;

class PoSeeAmbientProcessor extends AudioWorkletProcessor {
    constructor(options) {
        super();

        const config = options?.processorOptions ?? {};

        this.sampleRate = sampleRate;
        this.phase = new Float64Array(8);
        this.lfoPhase = new Float64Array(8);

        // Root of the drone. C2 by default — low enough to sit under speech and under every
        // foreground cue, which all live above C4.
        this.rootTarget = config.root ?? 65.41;
        this.root = this.rootTarget;

        // 0..1 strangeness. Drives how much of the dissonant upper structure is audible and how
        // fast the partials beat against each other.
        this.strangeTarget = config.strange ?? 0.3;
        this.strange = this.strangeTarget;

        // Master level for the bed. Starts at zero and is faded in by the host, so starting the
        // node is never itself an audible event.
        this.levelTarget = 0;
        this.level = 0;

        // Ducking. Set to 1 by the host just before a foreground cue and released after; the bed
        // gets out of the way rather than being mixed against. Attack is fast and release slow,
        // which is what a compressor would do and what the ear expects.
        this.duckTarget = 0;
        this.duck = 0;

        // Interval structure, as ratios against the root. The first three are a stable open
        // fifth stack; the last two only become audible as strangeness rises — a major seventh
        // and a tritone, matching the intervals audio.js's scoreLand adds for the same reason.
        this.ratios = [1, 1.5, 2, 3.7776, 2.8284];
        // Detune in cents, deliberately irrational against each other so the beating pattern
        // between partials never lines up and the bed never audibly loops.
        this.detune = [0, 3.1, -2.6, 5.7, -4.3];
        // LFO rates in Hz, likewise mutually prime-ish.
        this.lfoRate = [0.031, 0.047, 0.023, 0.017, 0.013];

        this.glide = Math.exp(-1 / (GLIDE_SECONDS * this.sampleRate));

        // Pink-ish noise state for the breath layer. Two one-pole filters in series on white
        // noise is not true pink, but it is the right SHAPE — energy falling with frequency —
        // and it costs two multiplies where a proper filter bank costs seven.
        this.noiseA = 0;
        this.noiseB = 0;

        this.running = true;

        this.port.onmessage = (event) => {
            const data = event.data ?? {};
            if (typeof data.root === 'number' && data.root > 0) this.rootTarget = data.root;
            if (typeof data.strange === 'number') this.strangeTarget = Math.min(1, Math.max(0, data.strange));
            if (typeof data.level === 'number') this.levelTarget = Math.min(1, Math.max(0, data.level));
            if (typeof data.duck === 'number') this.duckTarget = Math.min(1, Math.max(0, data.duck));
            if (data.stop === true) this.levelTarget = 0;
            // The host asks the node to end only after it has faded; `running` false lets
            // process() return false so the browser can collect it.
            if (data.dispose === true) this.running = false;
        };
    }

    process(inputs, outputs) {
        const output = outputs[0];
        if (!output || output.length === 0) return this.running;

        const left = output[0];
        const right = output.length > 1 ? output[1] : null;
        const frames = left.length;
        const g = this.glide;
        const invRate = 1 / this.sampleRate;

        for (let i = 0; i < frames; i++) {
            // One-pole smoothing toward every target.
            this.root = this.rootTarget + (this.root - this.rootTarget) * g;
            this.strange = this.strangeTarget + (this.strange - this.strangeTarget) * g;
            this.level = this.levelTarget + (this.level - this.levelTarget) * g;
            // Ducking uses its own, much faster coefficient: the point of a duck is to be out of
            // the way before the cue arrives, and a 0.9s glide would arrive after it had gone.
            this.duck += (this.duckTarget - this.duck) * (this.duckTarget > this.duck ? 0.004 : 0.00012);

            let sampleL = 0;
            let sampleR = 0;

            for (let v = 0; v < this.ratios.length; v++) {
                // Voices 3 and 4 are the dissonant ones and fade in with strangeness. Squaring
                // keeps them genuinely absent at low scores rather than merely quiet — a tritone
                // at 8% is still a tritone.
                let voiceGain = v < 3 ? 1 : this.strange * this.strange;
                if (voiceGain < 0.0005) {
                    // Still advance the phase, or the voice snaps to a different point in its
                    // cycle the moment it becomes audible.
                    this.phase[v] += (this.root * this.ratios[v] * invRate);
                    if (this.phase[v] >= 1) this.phase[v] -= 1;
                    continue;
                }

                const detuneRatio = 1 + this.detune[v] / 1200;
                const freq = this.root * this.ratios[v] * detuneRatio;

                this.phase[v] += freq * invRate;
                if (this.phase[v] >= 1) this.phase[v] -= 1;

                this.lfoPhase[v] += this.lfoRate[v] * invRate;
                if (this.lfoPhase[v] >= 1) this.lfoPhase[v] -= 1;

                // Slow amplitude drift per partial. This is what stops a stack of sines reading
                // as an organ chord: the balance between them is never the same twice.
                const lfo = 0.62 + 0.38 * Math.sin(this.lfoPhase[v] * TWO_PI);

                // Upper partials roll off. A flat stack sounds like a synth patch; 1/n is what a
                // physical resonator does.
                const rolloff = 1 / (1 + v * 0.85);

                const value = Math.sin(this.phase[v] * TWO_PI) * lfo * rolloff * voiceGain;

                // Constant-power spread across the stereo field, alternating side by voice
                // index. Root centred, everything else pushed out — same voicing logic as the
                // resolution chord, for the same reason: stacked in mono they beat in one spot.
                const pan = v === 0 ? 0 : ((v % 2 === 1 ? -1 : 1) * Math.min(0.8, 0.28 * v));
                const angle = (pan + 1) * 0.25 * Math.PI;
                sampleL += value * Math.cos(angle);
                sampleR += value * Math.sin(angle);
            }

            // Breath layer: two-pole-ish filtered noise, very quiet, rising slightly with
            // strangeness. Without it the bed is pure tone and reads as a test signal.
            const white = Math.random() * 2 - 1;
            this.noiseA += (white - this.noiseA) * 0.0009;
            this.noiseB += (this.noiseA - this.noiseB) * 0.0009;
            const breath = this.noiseB * (14 + this.strange * 16);

            const gain = this.level * (1 - this.duck * 0.75) * 0.16;
            left[i] = (sampleL * 0.32 + breath) * gain;
            if (right) right[i] = (sampleR * 0.32 + breath * 0.86) * gain;
        }

        // Keep processing while the host wants the node alive. Returning false here is permanent
        // — the node can never be restarted — so it is only done on an explicit dispose.
        return this.running;
    }
}

registerProcessor('posee-ambient', PoSeeAmbientProcessor);
