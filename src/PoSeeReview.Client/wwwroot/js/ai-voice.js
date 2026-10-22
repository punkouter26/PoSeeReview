// AI narrator voice: Kokoro-82M, synthesised in the browser, opt-in.
//
// WHY. The narrative is the joke, and "Read aloud" handed it to speechSynthesis, which on most
// machines is a flat system voice. Kokoro is an 82M-parameter TTS model that runs entirely on
// the visitor's device: nothing leaves the page, and the server pays nothing per reading.
//
// COST TO THE VISITOR. ~92MB of weights (q8) on first use, cached by the browser afterwards, so
// it is off by default and switched on from the account menu (`posee_ai_voice`). It runs in a
// Blob module worker on WebAssembly: on the main thread the session build stalls the frame loop
// (the lesson depth-diorama.js learned), and Kokoro's own guidance is fp32 (326MB) for WebGPU,
// which is no download to ask for. The page is not cross-origin isolated, so WASM is
// single-threaded and synthesis runs slower than speech — measured ~4.4x on a desktop CPU. So the
// comic page prepares the narrative as it opens (only with the voice switched on), its sentences
// are kept for replays, and a tap before they are ready plays each sentence as soon as it exists
// while the rest are synthesised. Any failure before the first sentence — no Worker, a CDN
// outage, a deadline — ends the stream empty, and audio.js reads the narrative with
// speechSynthesis instead. Nothing here throws.
//
// kokoro-js 1.2.1 (2025-05-03): its web build is self-contained (it bundles transformers.js 3.x
// and the phonemiser), so it is the one import.

const KOKORO_URL = 'https://cdn.jsdelivr.net/npm/kokoro-js@1.2.1/dist/kokoro.web.js';
const MODEL_ID = 'onnx-community/Kokoro-82M-v1.0-ONNX';
const VOICE = 'bm_george';          // A dry British narrator suits a caption box.
const STORAGE_KEY = 'posee_ai_voice';
const FIRST_USE_DEADLINE_MS = 180000;   // download + session build + first sentence
const SENTENCE_DEADLINE_MS = 120000;  // a long sentence on a slow CPU takes most of a minute
const IDLE_DISPOSE_MS = 180000;

const WORKER_SOURCE = `
let loading = null;
let current = 0;   // only the newest request is spoken; a stop or a new request ends the old one
self.onmessage = async (event) => {
    const msg = event.data;
    if (msg.cancel) {
        if (current === msg.id) current = 0;   // a late cancel must not end a newer request
        return;
    }
    current = msg.id;
    try {
        loading ??= (async () => {
            const { KokoroTTS } = await import(${JSON.stringify(KOKORO_URL)});
            return KokoroTTS.from_pretrained(${JSON.stringify(MODEL_ID)}, { dtype: 'q8', device: 'wasm' });
        })();
        const tts = await loading;
        const sentences = String(msg.text).match(/[^.!?]+[.!?]*["')\\]]*\\s*/g) ?? [msg.text];
        for (const sentence of sentences) {
            if (current !== msg.id) return;
            if (!sentence.trim()) continue;
            const audio = await tts.generate(sentence.trim(), { voice: ${JSON.stringify(VOICE)} });
            const samples = audio.audio instanceof Float32Array ? audio.audio : new Float32Array(audio.audio);
            self.postMessage({ id: msg.id, samples, sampleRate: audio.sampling_rate }, [samples.buffer]);
        }
        self.postMessage({ id: msg.id, done: true });
    } catch (err) {
        loading = null;
        self.postMessage({ id: msg.id, error: String(err && err.message || err) });
    }
};`;

// One synthesis at a time: the comic page's narrative. Its sentences are kept, so a second tap
// replays at once, and a page that prepared it on open has audio ready before the tap.
const state = {
    worker: null,
    warm: false,        // the model has produced audio once this visit
    failed: false,      // a load failed: stay on speechSynthesis for the rest of the visit
    job: null,          // { id, text, chunks, done, wake: Set }
    nextJob: 1,
    idleTimer: 0
};

/** The key a narrative is synthesised under: audio.js trims and caps the text the same way. */
const keyOf = (text) => String(text ?? '').trim().slice(0, 800);

function getWorker() {
    if (!state.worker) {
        const url = URL.createObjectURL(new Blob([WORKER_SOURCE], { type: 'text/javascript' }));
        state.worker = new Worker(url, { type: 'module' });
        URL.revokeObjectURL(url);
        state.worker.onmessage = (event) => {
            const msg = event.data ?? {};
            const job = state.job;
            if (!job || job.id !== msg.id) return;
            if (msg.samples) {
                state.warm = true;
                job.chunks.push({ samples: msg.samples, sampleRate: msg.sampleRate });
            } else {
                if (msg.error) {
                    console.warn('[ai-voice] synthesis failed; using the system voice', msg.error);
                    state.failed = true;
                }
                job.done = true;
                scheduleDispose();
            }
            for (const wake of job.wake) wake();
        };
        state.worker.onerror = (e) => {
            e.preventDefault?.();
            console.warn('[ai-voice] worker failed', e.message);
            state.failed = true;
            dispose();
        };
    }
    return state.worker;
}

function finish(job) {
    job.done = true;
    for (const wake of job.wake) wake();
}

function dispose() {
    try { state.worker?.terminate(); } catch { /* gone */ }
    state.worker = null;
    state.warm = false;
    if (state.job && !state.job.done) {
        finish(state.job);
        state.job = null;   // half-synthesised: start over next time
    }
}

function scheduleDispose() {
    clearTimeout(state.idleTimer);
    state.idleTimer = setTimeout(() => {
        if (!state.job || state.job.done) dispose();
    }, IDLE_DISPOSE_MS);
}

/** The job for this text: the current one if it matches, otherwise a new one replacing it. */
function start(text) {
    const key = keyOf(text);
    if (state.job?.text === key) return state.job;
    if (state.job && !state.job.done) {
        try { state.worker?.postMessage({ cancel: true, id: state.job.id }); } catch { /* gone */ }
        finish(state.job);
    }
    const job = { id: state.nextJob++, text: key, chunks: [], done: false, wake: new Set() };
    state.job = job;
    clearTimeout(state.idleTimer);
    try {
        getWorker().postMessage({ id: job.id, text: key });
    } catch {
        state.failed = true;
        finish(job);
    }
    return job;
}

export const aiVoice = {
    supported() {
        return typeof Worker === 'function' && typeof WebAssembly === 'object' && typeof Blob === 'function';
    },

    /** The stored preference. Off unless the visitor switched it on: it costs them a download. */
    isEnabled() {
        try {
            return localStorage.getItem(STORAGE_KEY) === 'true';
        } catch {
            return false;
        }
    },

    setEnabled(enabled) {
        try { localStorage.setItem(STORAGE_KEY, enabled ? 'true' : 'false'); } catch { /* not remembered */ }
        if (!enabled) {
            dispose();
            state.job = null;
        }
        return this.isEnabled();
    },

    /** Whether narration should go through this voice right now. */
    active() {
        return this.supported() && this.isEnabled() && !state.failed;
    },

    /** Starts synthesising ahead of the tap. The comic page calls it on open. */
    prepare(text) {
        if (this.active() && keyOf(text)) start(text);
    },

    /**
     * The text as speech, one `{ samples, sampleRate }` per sentence, in order — from the buffer
     * where it is already synthesised, then live. Ends early (or empty) on a failure or a
     * deadline; never throws.
     */
    async *stream(text) {
        if (!this.active() || !keyOf(text)) return;
        const job = start(text);
        let next = 0;
        while (true) {
            if (next < job.chunks.length) {
                yield job.chunks[next++];
                continue;
            }
            if (job.done) return;
            const deadline = next === 0 && !state.warm ? FIRST_USE_DEADLINE_MS : SENTENCE_DEADLINE_MS;
            const arrived = await new Promise((resolve) => {
                const wake = () => { job.wake.delete(wake); clearTimeout(timer); resolve(true); };
                const timer = setTimeout(() => { job.wake.delete(wake); resolve(false); }, deadline);
                job.wake.add(wake);
            });
            if (!arrived) return;
        }
    }
};
