// Web Audio API helpers for PoSeeReview
// Uses the browser's native AudioContext for UI feedback sounds.

(function () {
    let audioCtx = null;

    function getAudioContext() {
        if (!audioCtx) {
            audioCtx = new (window.AudioContext || window.webkitAudioContext)();
        }
        // Resume if suspended (browser autoplay policy)
        if (audioCtx.state === 'suspended') {
            audioCtx.resume();
        }
        return audioCtx;
    }

    function playTone(frequency, duration, type, volume) {
        try {
            const ctx = getAudioContext();
            const oscillator = ctx.createOscillator();
            const gainNode = ctx.createGain();

            oscillator.type = type || 'sine';
            oscillator.frequency.setValueAtTime(frequency, ctx.currentTime);

            gainNode.gain.setValueAtTime(volume || 0.1, ctx.currentTime);
            gainNode.gain.exponentialRampToValueAtTime(0.001, ctx.currentTime + duration);

            oscillator.connect(gainNode);
            gainNode.connect(ctx.destination);

            oscillator.start(ctx.currentTime);
            oscillator.stop(ctx.currentTime + duration);
        } catch (e) {
            // Silently fail if Web Audio API is not available
            console.debug('Audio playback not available:', e);
        }
    }

    // Success sound: ascending two-tone chime
    window.playSuccessSound = function () {
        playTone(523.25, 0.15, 'sine', 0.08); // C5
        setTimeout(() => playTone(659.25, 0.2, 'sine', 0.08), 100); // E5
    };

    // Error sound: low buzz
    window.playErrorSound = function () {
        playTone(200, 0.3, 'sawtooth', 0.05);
    };

    // Click sound: short high tick
    window.playClickSound = function () {
        playTone(800, 0.05, 'sine', 0.03);
    };
})();
