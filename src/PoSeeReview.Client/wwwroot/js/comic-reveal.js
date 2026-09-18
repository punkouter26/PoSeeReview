// The comic develops onto the page instead of appearing on it.
//
// THE MOMENT THIS IS FOR. A generation is about ten seconds of stepper, and then the finished
// strip pops into the layout in one frame. That single frame is the payoff of the entire wait and
// it was being spent on a layout change. Developing the strip top-down over ~1.4s costs nothing,
// delays nothing the user can act on, and turns the arrival into the thing they waited for.
//
// A NOTE ON WHAT THIS IS *NOT* DRIVEN BY. It would be neater to wipe each panel in as its
// pipeline phase arrives over the SSE stream — but the artwork does not exist until `Publishing`.
// Every phase before it is text and metadata, so there is nothing to reveal while they run. The
// reveal therefore runs on ARRIVAL, and the stepper keeps narrating the wait itself.
//
// TWO LAYERS, AND THE CSS ONE IS THE IMPORTANT ONE.
//
//   1. A CSS mask on the container, driven by the `--comic-reveal` custom property this module
//      writes each frame. This ALWAYS runs (above the `off` tier) and needs no WebGL, so it is
//      what nearly everyone actually sees — comic-fx only attaches when the blob happens to be
//      CORS-readable, which in this deployment it usually is not.
//   2. The shader's own ink-threshold mask, when comic-fx did attach, forwarded through
//      setReveal. That adds the ragged wet-ink boundary the CSS gradient cannot express.
//
// The property is written from JS rather than from a `style` attribute in the Razor markup
// because this repo bans inline styles, and because a per-frame value belongs in the animation
// loop rather than in the render tree — a Blazor StateHasChanged per frame to move a mask would
// cost more than the mask.
//
// EVERY EXIT PATH CLEANS UP THE ATTRIBUTE. The mask only applies while `data-comic-reveal` is
// present, so leaving it behind on an early return would hide part of the comic permanently.
// That is the one failure mode here that the user would actually notice.

import { gfx } from './gfx-core.js';

const PROPERTY = '--comic-reveal';
const ATTRIBUTE = 'comicReveal';   // dataset key for data-comic-reveal

const instances = new Map();
let nextId = 1;

/**
 * Ease for the development. Slow start, quick middle, soft landing — ink wicking into paper
 * accelerates as the wet front widens and then stalls as it dries, and a linear wipe reads as a
 * loading bar rather than as a material.
 */
function ease(t) {
    return t < 0.5
        ? 4 * t * t * t
        : 1 - Math.pow(-2 * t + 2, 3) / 2;
}

/**
 * @param {HTMLElement} container the .comic-strip-container; carries the mask and the property
 * @param {{ bands?: number, durationMs?: number, fxHandle?: number,
 *           onBand?: (index: number, total: number) => void }} options
 * @returns {number} handle, or 0 when no reveal ran (in which case the comic is simply visible)
 */
export function start(container, options = {}) {
    if (!container) return 0;

    // `off` is reduced motion or no WebGL2. Someone who asked their OS for less movement is not
    // asking for a slower comic, so they get it immediately and completely.
    if (!gfx.allows('lite')) return 0;

    const bands = Math.max(1, Math.min(4, options.bands ?? 2));
    const durationMs = Math.max(300, Math.min(4000, options.durationMs ?? 1400));
    const fxHandle = options.fxHandle ?? 0;
    const onBand = typeof options.onBand === 'function' ? options.onBand : null;

    // The wet-ink field, when one started (WebGPU only — see ink-field.js). It is a SUBSTITUTE
    // for the CSS mask, not a layer on top of it: the field covers the un-developed comic in
    // paper colour and eats the cover away, so running the mask as well would develop the comic
    // twice and leave a visible seam where the two boundaries disagreed. This loop still owns the
    // timing and the cues either way — the field decides what the edge LOOKS like, not when the
    // reveal is over.
    const onProgress = typeof options.onProgress === 'function' ? options.onProgress : null;
    const suppressMask = options.suppressMask === true;

    // A second start on the same container replaces the first — a regenerate swaps the src on
    // the same element, and two loops writing one property is a fight neither wins.
    for (const [existingId, existing] of instances) {
        if (existing.container === container) cancel(existingId);
    }

    const id = nextId++;
    const startedAt = performance.now();

    const instance = {
        container,
        fxHandle,
        bandsAnnounced: 0,
        stop: null
    };

    const write = (value) => {
        try {
            container.style.setProperty(PROPERTY, value.toFixed(4));
        } catch {
            // Element detached mid-flight; the frame task tears down on the next tick.
        }
    };

    try {
        // The attribute is what makes the CSS mask apply at all, so withholding it is how the
        // field takes over cleanly — no rule to disable, nothing to keep in sync.
        if (!suppressMask) {
            container.dataset[ATTRIBUTE] = 'running';
            write(0);
        }
    } catch {
        return 0;
    }

    instance.stop = gfx.addTask(`comic-reveal#${id}`, (now) => {
        // The element can be removed from the DOM by a navigation while the loop is live.
        if (!container.isConnected) {
            cancel(id);
            return;
        }

        const raw = Math.min(1, (now - startedAt) / durationMs);
        const progress = ease(raw);

        if (!suppressMask) write(progress);
        if (fxHandle) setShaderReveal(fxHandle, progress);
        if (onProgress) {
            try {
                onProgress(progress);
            } catch {
                // The field failing must not stall the reveal — and if it has stopped drawing,
                // suppressMask means there is no mask either, so the comic is simply visible.
            }
        }

        // Announce each band as its development passes the halfway mark, not as it starts:
        // a panel is recognisable at half-developed, and a cue that fires when the first
        // speckles appear lands before there is anything to look at.
        const crossed = Math.floor(progress * bands + 0.5);
        while (instance.bandsAnnounced < Math.min(crossed, bands)) {
            const index = instance.bandsAnnounced++;
            if (onBand) {
                try {
                    onBand(index, bands);
                } catch {
                    // A cue failing must not stall the reveal.
                }
            }
        }

        if (raw >= 1) {
            finish(id);
        }
    });

    instances.set(id, instance);
    return id;
}

/**
 * Ends the reveal with the comic fully developed. Separate from cancel() only in intent — both
 * land on the same cleanup, because a half-finished reveal and a finished one must both leave
 * the whole comic visible.
 */
export function finish(id) {
    const instance = instances.get(id);
    if (!instance) return;

    if (instance.fxHandle) setShaderReveal(instance.fxHandle, 1);
    cleanup(instance);
    instance.stop?.();
    instances.delete(id);
}

export function cancel(id) {
    finish(id);
}

function cleanup(instance) {
    try {
        // Remove the attribute BEFORE clearing the property. The mask rule is keyed on the
        // attribute, so dropping it first means there is never a frame where a masked element
        // has no reveal value — which would resolve to the @property initial value and flash.
        delete instance.container.dataset[ATTRIBUTE];
        instance.container.style.removeProperty(PROPERTY);
    } catch {
        // Already detached.
    }
}

/** Reveals in progress. Read by the perf HUD and the diagnostics page. */
export function activeCount() {
    return instances.size;
}

gfx.onTierChanged((tier) => {
    // Dropping to `off` mid-reveal must not freeze the comic behind a half-drawn mask.
    if (tier === 'off') {
        for (const id of [...instances.keys()]) finish(id);
    }
});
