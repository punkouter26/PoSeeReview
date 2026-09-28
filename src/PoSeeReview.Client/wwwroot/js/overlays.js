// Transient comic-book furniture laid over the page: sound-word stamps ("POW!") and the speech
// bubbles that give the skit's voices faces.
//
// Both are plain DOM with CSS animations. Stamps live in a fixed layer on <body>, outside #app,
// so Blazor never diffs them; bubbles go into the Razor-rendered .skit-layer inside the strip,
// which Razor renders empty and never touches again. Nothing here makes a sound — fx.js pairs
// each with its cue, for the reason every coupling in this layer goes through fx.js.

import { gfx } from './gfx-core.js';

let stampLayer = null;

function starburst(points = 14) {
    const coords = [];
    for (let i = 0; i < points * 2; i++) {
        const angle = (i / (points * 2)) * Math.PI * 2 - Math.PI / 2;
        // Uneven spikes: a regular star reads as a badge, a ragged one as an impact.
        const radius = i % 2 === 0 ? 48 - (i % 4) * 2.5 : 32;
        coords.push(`${(50 + Math.cos(angle) * radius).toFixed(1)},${(50 + Math.sin(angle) * radius).toFixed(1)}`);
    }
    return `<svg viewBox="0 0 100 100" class="fx-stamp-burst"><polygon points="${coords.join(' ')}"/></svg>`;
}

/**
 * Stamps a sound word at a point. `target` is an element (stamped at its centre) or a viewport x
 * with `y` beside it. Returns the viewport x it landed at, so the caller can pan the thwack.
 */
export function stamp(word, target, y) {
    if (!gfx.allows('lite')) return null;

    let x = target;
    if (target instanceof Element) {
        const rect = target.getBoundingClientRect();
        x = rect.left + rect.width / 2;
        y = rect.top + rect.height / 2;
    }
    if (!Number.isFinite(x) || !Number.isFinite(y)) return null;

    if (!stampLayer?.isConnected) {
        stampLayer = document.createElement('div');
        stampLayer.className = 'fx-stamp-layer';
        stampLayer.setAttribute('aria-hidden', 'true');
        document.body.appendChild(stampLayer);
    }

    // Kept inside the viewport: a stamp hanging off the edge is cut in half by the fixed layer.
    const margin = 70;
    x = Math.max(margin, Math.min(window.innerWidth - margin, x));
    y = Math.max(margin, Math.min(window.innerHeight - margin, y));

    const el = document.createElement('div');
    el.className = 'fx-stamp';
    el.innerHTML = `${starburst()}<span class="fx-stamp-word"></span>`;
    el.querySelector('.fx-stamp-word').textContent = String(word ?? '').slice(0, 12);
    el.style.setProperty('--stamp-x', `${x}px`);
    el.style.setProperty('--stamp-y', `${y}px`);
    el.style.setProperty('--stamp-rot', `${(Math.random() * 28 - 14).toFixed(1)}deg`);
    stampLayer.appendChild(el);
    el.addEventListener('animationend', (e) => { if (e.animationName === 'posee-stamp-out') el.remove(); });
    // Belt and braces for a browser that skips animationend (backgrounded tab, reduced motion).
    setTimeout(() => el.remove(), 2000);
    return x;
}

// ── Skit bubbles ─────────────────────────────────────────────────────────────────────────────

/**
 * One bubble per line, placed over the panel that line falls in and on its speaker's side, so a
 * two-hander alternates left and right the way a strip is lettered.
 *
 * @param {HTMLElement} strip .comic-strip-container (holds the .skit-layer)
 * @param {{ panels?: number }} options
 */
export function bubbles(strip, options = {}) {
    const layer = strip?.querySelector?.('.skit-layer');
    if (!layer) return null;
    const panels = Math.max(1, options.panels ?? 2);
    clearBubbles(layer);

    return {
        /** @returns {'left'|'right'} the side it appeared on */
        show(index, total, speaker, slot, text) {
            for (const old of layer.querySelectorAll('.skit-bubble')) old.dataset.leaving = 'true';
            const side = slot % 2 === 0 ? 'left' : 'right';
            const panel = Math.min(panels - 1, Math.floor((index * panels) / Math.max(1, total)));

            const bubble = document.createElement('div');
            bubble.className = 'skit-bubble';
            bubble.dataset.side = side;
            bubble.style.setProperty('--bubble-top', `${((panel + 0.08) / panels) * 100}%`);
            const who = document.createElement('strong');
            who.textContent = speaker;
            const said = document.createElement('span');
            said.textContent = text;
            bubble.append(who, said);
            layer.appendChild(bubble);

            // Leaving bubbles go once their exit has had time to play.
            setTimeout(() => {
                for (const old of layer.querySelectorAll('.skit-bubble[data-leaving]')) old.remove();
            }, 400);
            return side;
        },
        end(index, total) {
            if (index >= total - 1) {
                setTimeout(() => clearBubbles(layer), 900);
            }
        }
    };
}

export function clearBubbles(layer) {
    const layers = layer ? [layer] : document.querySelectorAll('.skit-layer');
    for (const l of layers) l.replaceChildren();
}
