// Procedural paper grain, generated once and handed to CSS.
//
// WHY THIS COSTS NO FRAME BUDGET, WHICH IS THE ENTIRE POINT.
//
// /my-comics is a grid of thumbnails on their way to expiring. Ageing them convincingly wants
// grain and foxing, and the obvious implementations are all wrong here:
//
//   * A WebGL pass per thumbnail — a dozen surfaces out of a pool capped at eight, for a page
//     that is a list of links.
//   * One canvas over the grid, masked to each card — it has to track layout, so every scroll
//     and every resize becomes a redraw.
//   * An SVG feTurbulence filter — re-evaluated by the compositor on every paint of a filtered
//     element, and it is applied to images that are already the heaviest thing on the page.
//
// Instead: build ONE small tileable noise texture, once, and hand it to CSS as a data URI in a
// custom property. After that the ageing is `background-image` plus `mix-blend-mode` — ordinary
// painting the browser was going to do anyway, with no rAF task, no observer, and nothing
// registered with the shared scheduler at all.
//
// TILEABILITY IS NOT OPTIONAL. A non-tiling noise tile shows a visible grid across a card, which
// reads as a rendering artifact rather than as paper. The value noise below is sampled on a torus
// — each axis wraps at the tile edge — so the seam is mathematically absent rather than hidden.

const TILE = 128;
const VAR_NAME = '--paper-grain';

let applied = false;

/** Integer hash on a wrapped lattice. The wrap is what makes the tile seamless. */
function hash(x, y) {
    const ix = ((x % TILE) + TILE) % TILE;
    const iy = ((y % TILE) + TILE) % TILE;
    let h = (ix * 374761393 + iy * 668265263) | 0;
    h = (h ^ (h >>> 13)) | 0;
    h = Math.imul(h, 1274126177) | 0;
    return ((h ^ (h >>> 16)) >>> 0) / 4294967295;
}

function smoothNoise(x, y, scale) {
    const fx = x / scale;
    const fy = y / scale;
    const x0 = Math.floor(fx);
    const y0 = Math.floor(fy);
    const tx = fx - x0;
    const ty = fy - y0;
    // Smoothstep weights, so the lattice does not show as diamonds.
    const ux = tx * tx * (3 - 2 * tx);
    const uy = ty * ty * (3 - 2 * ty);

    const a = hash(x0, y0);
    const b = hash(x0 + 1, y0);
    const c = hash(x0, y0 + 1);
    const d = hash(x0 + 1, y0 + 1);

    return (a * (1 - ux) + b * ux) * (1 - uy) + (c * (1 - ux) + d * ux) * uy;
}

/**
 * Builds the texture and publishes it as a CSS custom property on the document element.
 *
 * Returns false, and writes nothing, on any failure — a blocked canvas, a tainted context, a
 * browser that will not encode a PNG. The ageing rules in app.css all resolve `var(--paper-grain,
 * none)`, so nothing here is load-bearing: a card with no grain is simply a card with no grain.
 *
 * Idempotent. Called from fx.js on load; a second call is free.
 */
export function install() {
    if (applied) return true;

    try {
        const canvas = document.createElement('canvas');
        canvas.width = TILE;
        canvas.height = TILE;
        const ctx = canvas.getContext('2d');
        if (!ctx) return false;

        const image = ctx.createImageData(TILE, TILE);
        const data = image.data;

        for (let y = 0; y < TILE; y++) {
            for (let x = 0; x < TILE; x++) {
                // Two octaves at coprime-ish scales. The fine one is the tooth of the paper; the
                // coarse one is the blotchiness that makes it read as aged rather than merely
                // noisy — even grain looks like film, uneven grain looks like damp.
                const fine = smoothNoise(x, y, 2);
                const coarse = smoothNoise(x, y, 16);
                const value = fine * 0.45 + coarse * 0.55;

                const i = (y * TILE + x) * 4;
                // Black everywhere; the NOISE is in the alpha. That is what lets one texture work
                // over any colour of card in either theme — a grey-pixel texture would wash a
                // dark card and darken a light one by different amounts.
                data[i] = 0;
                data[i + 1] = 0;
                data[i + 2] = 0;
                data[i + 3] = Math.round(Math.pow(value, 1.6) * 96);
            }
        }

        ctx.putImageData(image, 0, 0);
        document.documentElement.style.setProperty(VAR_NAME, `url("${canvas.toDataURL('image/png')}")`);
        applied = true;
        return true;
    } catch {
        return false;
    }
}

/** Whether the grain is available, for the diagnostics panel. */
export function isInstalled() {
    return applied;
}
