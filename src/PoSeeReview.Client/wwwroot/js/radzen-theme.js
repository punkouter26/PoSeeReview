// Radzen's theme and script, loaded only by the operator pages (/moderation, /diagnostics) that
// use Radzen components — never by the consumer app, which has its own design system.
//
// The theme is fetched and wrapped in @layer rather than linked, for the same reason as MapLibre's
// (map-view.js): an unlayered sheet beats every layer in app.css. In `vendor` it sits below `page`
// and `shared`, so app.css's `--rz-*` bridge (brand colours, dark mode) wins. The `-base` theme is
// the one whose only element rule is `body { margin: 0 }`; the full theme restyles h1-h6 and body.

const BASE = '_content/Radzen.Blazor/';

let loading;

export function load() {
    loading ??= Promise.all([loadTheme(), loadScript()]);
    return loading;
}

async function loadTheme() {
    if (document.querySelector('style[data-vendor="radzen"]')) return;
    const response = await fetch(`${BASE}css/material-base.css`);
    if (!response.ok) throw new Error(`Radzen theme ${response.status}`);
    // Its font URLs are relative to the sheet; inlined, they would resolve against the page.
    const css = (await response.text()).replace(/^﻿/, '').replaceAll('url("../fonts/', `url("${BASE}fonts/`);
    const style = document.createElement('style');
    style.dataset.vendor = 'radzen';
    style.textContent = `@layer vendor {\n${css}\n}`;
    document.head.append(style);
}

function loadScript() {
    if (window.Radzen) return Promise.resolve();
    return new Promise((resolve, reject) => {
        const script = document.createElement('script');
        script.src = `${BASE}Radzen.Blazor.min.js`;
        script.onload = resolve;
        script.onerror = () => reject(new Error('Radzen script failed to load'));
        document.head.append(script);
    });
}
