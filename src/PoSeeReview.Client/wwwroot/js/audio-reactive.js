// Bridges the audio analyser to the shaders that can respond to it.
//
// This is deliberately a separate module rather than a call inside gradient.js, for one reason:
// the coupling should point one way. The gradient must render correctly with the sound off —
// that is its default state and how most people will see it — so it exposes a setter and knows
// nothing about audio. This module is the only thing that knows both sides exist, and if it
// never runs, nothing downstream notices.
//
// It also means the analyser costs nothing when it is not needed. getByteFrequencyData copies
// the FFT into a typed array every call; polling that at 60Hz for a page where audio is off and
// the level is structurally zero is pure waste. The driver only registers a task while audio is
// actually enabled and unlocked.

import { gfx } from './gfx-core.js';
import { audio } from './audio.js';
import * as gradient from './gradient.js';

// 30Hz. The visual response is heavily smoothed on both sides, so sampling every frame buys
// nothing a viewer can see — and this runs on the same thread as the rendering it modulates.
const SAMPLE_INTERVAL_MS = 33;

const state = {
    stop: null,
    lastSample: 0,
    running: false
};

function tick(now) {
    if (now - state.lastSample < SAMPLE_INTERVAL_MS) return;
    state.lastSample = now;

    const spectrum = audio.analyse();
    for (const id of gradient.activeIds()) {
        gradient.setAudioLevels(id, spectrum.level, spectrum.bass);
    }
}

/** Starts driving visuals from audio. Idempotent. */
export function start() {
    if (state.running) return;
    state.running = true;
    state.stop = gfx.addTask('audio-reactive', tick);
}

/**
 * Stops, and decays every gradient back to silence rather than freezing it at whatever level the
 * last sample happened to catch. A backdrop left permanently bright because the user muted mid
 * chord is a bug that would be very hard to attribute later.
 */
export function stop() {
    if (!state.running) return;
    state.running = false;
    state.stop?.();
    state.stop = null;

    for (const id of gradient.activeIds()) {
        gradient.setAudioLevels(id, 0, 0);
    }
}

/** Follows the audio enable state. Called by fx.js wherever audio is turned on or off. */
export function sync(enabled) {
    if (enabled) {
        start();
    } else {
        stop();
    }
}
