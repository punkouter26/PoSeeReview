// Publishes a comic's own colours to CSS, so the page around the strip can wear them.
//
// The three hex values come from the server (ComicPaletteExtractor) rather than from the pixels
// in the browser, and that is not a preference: the comic blob is served without CORS headers,
// so a canvas that has drawn it is tainted and cannot be read back. It is the same constraint
// that stops comic-fx.js attaching for most visitors.
//
// WHAT THE TINT IS ALLOWED TO TOUCH, AND WHY IT IS A SHORT LIST.
//
// Only accents: the score ring stroke, the ring's glow, reaction chip edges, the comic card's
// rim. Never a text colour and never a text background. The extracted colours are whatever the
// model drew — they can come back as a mid-yellow or a near-black, and the app has no way to
// know in advance. Every text/surface pair in this app is measured against WCAG by
// ColorContrastTests reading the real token values out of app.css; a colour invented at runtime
// by an image model is exactly the thing that test cannot cover. So the tint decorates edges
// that carry no information on their own, and every readable pair stays on the audited tokens.
//
// Three variables plus a flag, cleared on every route change by the caller. The flag exists so
// CSS can gate on `:root[data-comic-tint]` and a page that never ran this is untouched rather
// than falling back through a var() chain on every painted element.

const VARS = ['--comic-tint-1', '--comic-tint-2', '--comic-tint-3'];
const FLAG = 'comicTint';

const HEX = /^#?([0-9a-f]{6})$/i;

function normalise(value) {
    const match = HEX.exec(String(value ?? '').trim());
    return match ? `#${match[1].toLowerCase()}` : null;
}

/**
 * Applies a palette. Returns false and leaves the page untouched for anything that is not three
 * readable hex colours — an older comic, or an image the extractor could not decode, both of
 * which render with the brand tokens exactly as they did before this existed.
 */
export function apply(palette) {
    const colors = (Array.isArray(palette) ? palette : []).map(normalise);
    if (colors.length < VARS.length || colors.some(c => c === null)) {
        clear();
        return false;
    }

    try {
        const root = document.documentElement;
        for (let i = 0; i < VARS.length; i++) {
            root.style.setProperty(VARS[i], colors[i]);
        }
        root.dataset[FLAG] = 'on';
        return true;
    } catch {
        // A style write failing is not worth reporting. The page renders on brand tokens.
        return false;
    }
}

/**
 * Removes the tint. MUST be called when leaving a comic: these are set on the document element,
 * so a tint left behind follows the user to the leaderboard and tints a page that has no comic
 * to justify it.
 */
export function clear() {
    try {
        const root = document.documentElement;
        for (const name of VARS) {
            root.style.removeProperty(name);
        }
        delete root.dataset[FLAG];
    } catch {
        // Nothing to undo.
    }
}

/** Whether a tint is currently applied. Used by the diagnostics panel. */
export function isApplied() {
    try {
        return document.documentElement.dataset[FLAG] === 'on';
    } catch {
        return false;
    }
}
