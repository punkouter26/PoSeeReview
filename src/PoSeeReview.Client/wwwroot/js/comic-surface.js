// The comic as a physical object: foil that catches the light, a loupe, a ripple when the score
// lands, and a page that misbehaves when the score is absurd.
//
// NONE OF THIS READS A PIXEL. The comic blob is cross-origin with no CORS headers, which is the
// wall every WebGL effect over the artwork hit: a tainted texture cannot be sampled. CSS has no
// such wall — a `background-image: url(blob)` and an SVG displacement filter both draw the image
// without ever handing its bytes to script. So JS here only writes custom properties and
// attributes, and app.css does all the drawing. No rAF loop, nothing on the frame budget.
//
// Every element this module touches inside the strip (.holo-foil, .comic-loupe) is rendered by
// Razor and merely styled from here, so Blazor's diff never meets a node it did not create.

import { gfx } from './gfx-core.js';

const surfaces = new Map();
let nextId = 1;

const clamp01 = (v) => Math.max(0, Math.min(1, v));
const panOf = (x) => Math.max(-0.7, Math.min(0.7, x * 1.4 - 0.7));

/**
 * @param {HTMLElement} container .comic-strip-container
 * @param {HTMLImageElement} image the real <img>, whose URL the loupe magnifies
 * @param {{ holo?: boolean, onShimmer?: (intensity: number, pan: number) => void,
 *           onLoupeMove?: (x: number, y: number) => void }} options
 */
export function start(container, image, options = {}) {
    if (!container || !gfx.allows('lite')) return 0;

    const lens = container.querySelector('.comic-loupe');
    const holo = options.holo === true && Boolean(container.querySelector('.holo-foil'));
    const surface = { container, image, lens, holo, loupe: false, last: null, listeners: [] };

    const on = (target, type, fn, opts) => {
        target.addEventListener(type, fn, opts);
        surface.listeners.push(() => target.removeEventListener(type, fn, opts));
    };

    if (holo) {
        container.dataset.foil = 'on';
        on(container, 'pointermove', (e) => {
            const rect = container.getBoundingClientRect();
            aimFoil(surface, (e.clientX - rect.left) / rect.width, (e.clientY - rect.top) / rect.height, options.onShimmer);
        });
        on(container, 'pointerleave', () => restFoil(surface));

        // Phones have no hover, but they have a tilt. The first reading is the baseline, so the
        // foil responds to how the phone MOVES rather than to how it happens to be held.
        // iOS gates this behind a permission prompt; it keeps the resting foil instead.
        if (window.matchMedia?.('(hover: none)').matches && 'DeviceOrientationEvent' in window) {
            let base = null;
            on(window, 'deviceorientation', (e) => {
                if (e.gamma == null || e.beta == null) return;
                base ??= { g: e.gamma, b: e.beta };
                aimFoil(surface, 0.5 + (e.gamma - base.g) / 40, 0.5 + (e.beta - base.b) / 40, options.onShimmer);
            });
        }
    }

    if (lens) {
        const move = (e) => {
            if (!surface.loupe) return;
            const rect = container.getBoundingClientRect();
            aimLoupe(surface, e.clientX - rect.left, e.clientY - rect.top, rect);
            options.onLoupeMove?.((e.clientX - rect.left) / rect.width, (e.clientY - rect.top) / rect.height);
        };
        on(container, 'pointermove', move);
        on(container, 'pointerdown', move);
    }

    const id = nextId++;
    surfaces.set(id, surface);
    return id;
}

function aimFoil(surface, x, y, onShimmer) {
    x = clamp01(x);
    y = clamp01(y);
    const style = surface.container.style;
    style.setProperty('--foil-x', `${(x * 100).toFixed(1)}%`);
    style.setProperty('--foil-y', `${(y * 100).toFixed(1)}%`);
    // Tilt away from the light: the axis is perpendicular to the offset from centre.
    const dx = x - 0.5;
    const dy = y - 0.5;
    style.setProperty('--foil-ax', (-dy).toFixed(3));
    style.setProperty('--foil-ay', dx.toFixed(3));
    style.setProperty('--foil-angle', `${(Math.hypot(dx, dy) * 12).toFixed(2)}deg`);

    if (surface.last && onShimmer) {
        const speed = Math.hypot(x - surface.last.x, y - surface.last.y);
        if (speed > 0.015) onShimmer(Math.min(1, speed * 10), panOf(x));
    }
    surface.last = { x, y };
}

function restFoil(surface) {
    const style = surface.container.style;
    for (const name of ['--foil-x', '--foil-y', '--foil-ax', '--foil-ay', '--foil-angle']) {
        style.removeProperty(name);
    }
    surface.last = null;
}

const LOUPE_ZOOM = 2.4;

function aimLoupe(surface, px, py, rect) {
    const size = surface.lens.offsetWidth || 140;
    const style = surface.container.style;
    style.setProperty('--loupe-x', `${px - size / 2}px`);
    style.setProperty('--loupe-y', `${py - size / 2}px`);
    style.setProperty('--loupe-bw', `${rect.width * LOUPE_ZOOM}px`);
    style.setProperty('--loupe-bh', `${rect.height * LOUPE_ZOOM}px`);
    style.setProperty('--loupe-bx', `${-(px * LOUPE_ZOOM - size / 2)}px`);
    style.setProperty('--loupe-by', `${-(py * LOUPE_ZOOM - size / 2)}px`);
}

/** @returns {boolean} whether the loupe is now on */
export function toggleLoupe(id) {
    const surface = surfaces.get(id);
    if (!surface?.lens) return false;

    surface.loupe = !surface.loupe;
    const { container, image } = surface;
    if (surface.loupe) {
        container.style.setProperty('--loupe-src', `url("${(image.currentSrc || image.src).replace(/"/g, '%22')}")`);
        const rect = container.getBoundingClientRect();
        aimLoupe(surface, rect.width / 2, rect.height / 3, rect);
        container.dataset.loupe = 'on';
    } else {
        delete container.dataset.loupe;
    }
    return surface.loupe;
}

/**
 * A displacement ripple through the artwork. The filter's scale is animated by SMIL inside the
 * SVG, so the browser runs it without a JS frame, and the filter is only attached for the second
 * it runs — an idle displacement filter would still be re-evaluated on every paint.
 */
export function wobble(id, score = 50) {
    const surface = surfaces.get(id);
    if (!surface) return;

    const defs = ensureWobbleFilter();
    const animate = defs.querySelector('animate');
    const depth = Math.round(6 + clamp01((score - 40) / 60) * 34);
    animate.setAttribute('values', `0;${depth};${Math.round(depth * 0.35)};0`);

    surface.container.dataset.wobble = 'on';
    try { animate.beginElement(); } catch { /* SMIL unavailable: the attribute times out below */ }
    clearTimeout(surface.wobbleTimer);
    surface.wobbleTimer = setTimeout(() => delete surface.container.dataset.wobble, 1300);
}

function ensureWobbleFilter() {
    let svg = document.getElementById('posee-fx-defs');
    if (svg) return svg;

    // Outside #app, so Blazor never renders over it.
    svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    svg.id = 'posee-fx-defs';
    svg.setAttribute('class', 'fx-defs');
    svg.setAttribute('aria-hidden', 'true');
    svg.innerHTML = `
        <filter id="posee-wobble" x="-5%" y="-5%" width="110%" height="110%">
            <feTurbulence type="fractalNoise" baseFrequency="0.011 0.018" numOctaves="2" seed="7" result="n">
                <animate attributeName="baseFrequency" dur="1.2s" values="0.011 0.018;0.02 0.009;0.011 0.018" repeatCount="indefinite"/>
            </feTurbulence>
            <feDisplacementMap in="SourceGraphic" in2="n" scale="0" xChannelSelector="R" yChannelSelector="G">
                <animate attributeName="scale" dur="1.2s" begin="indefinite" values="0;20;7;0" fill="remove"/>
            </feDisplacementMap>
        </filter>`;
    document.body.appendChild(svg);
    return svg;
}

export function stop(id) {
    const surface = surfaces.get(id);
    if (!surface) return;
    surfaces.delete(id);

    for (const remove of surface.listeners) remove();
    clearTimeout(surface.wobbleTimer);
    const { container } = surface;
    delete container.dataset.foil;
    delete container.dataset.loupe;
    delete container.dataset.wobble;
    restFoil(surface);
}

// ── Weirdness ────────────────────────────────────────────────────────────────────────────────
//
// For an absurd score the page itself stops behaving. Stamped on <html> as data-weird so app.css
// owns the twitching; JS adds only the ink the pointer leaves behind, which needs positions.

export const WEIRD_THRESHOLD = 85;

let trail = null;

/** @returns {boolean} whether the page went weird */
export function setWeird(score) {
    if (score < WEIRD_THRESHOLD || !gfx.allows('lite')) return false;
    document.documentElement.dataset.weird = 'on';

    // The trail is for a mouse: on touch there is no pointer wandering between taps to trace.
    if (!trail && gfx.allows('full') && window.matchMedia?.('(hover: hover) and (pointer: fine)').matches) {
        const layer = document.createElement('div');
        layer.className = 'ink-trail-layer';
        layer.setAttribute('aria-hidden', 'true');
        document.body.appendChild(layer);

        let lastAt = 0;
        const onMove = (e) => {
            const now = performance.now();
            if (now - lastAt < 35 || layer.childElementCount >= 24) return;
            lastAt = now;
            const drop = document.createElement('span');
            drop.className = 'ink-trail-drop';
            drop.style.setProperty('--drop-x', `${e.clientX}px`);
            drop.style.setProperty('--drop-y', `${e.clientY}px`);
            drop.style.setProperty('--drop-size', `${4 + Math.random() * 7}px`);
            drop.addEventListener('animationend', () => drop.remove(), { once: true });
            layer.appendChild(drop);
        };
        document.addEventListener('pointermove', onMove, { passive: true });
        trail = { layer, onMove };
    }
    return true;
}

export function clearWeird() {
    delete document.documentElement.dataset.weird;
    if (trail) {
        document.removeEventListener('pointermove', trail.onMove);
        trail.layer.remove();
        trail = null;
    }
}

gfx.onTierChanged((tier) => {
    if (tier === 'off') {
        clearWeird();
        for (const id of [...surfaces.keys()]) stop(id);
    }
});
