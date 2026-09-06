// Real-time performance overlay.
//
// The numbers behind this already existed, but only on /diagnostics — a page with no nav entry,
// polling over JS interop on a 500ms .NET timer. That is the wrong instrument for the job in two
// ways: you cannot watch the metric on the page that is actually slow, and the act of measuring
// goes through the interop layer you are trying to characterise.
//
// This draws straight into a 2D canvas from inside the shared rAF loop, so it observes the same
// frames the effects do. It is off by default and costs nothing until switched on.
//
// COST HONESTY. An overlay that redraws every frame is itself a source of the jank it reports —
// filling text on a canvas is not free. So it repaints at 10Hz while sampling at 60Hz: the
// sparkline still shows every frame, the numbers just settle at a rate a human can read.
//
// Enable with ?fx=debug, Ctrl+Shift+F, or poseeFx.togglePerfHud().

import { gfx } from './gfx-core.js';
import { audio } from './audio.js';

const WIDTH = 216;
const HEIGHT = 188;
const REPAINT_MS = 100;
const SPARK_HEIGHT = 34;
const BUDGET_MS = 20;

const state = {
    canvas: null,
    ctx: null,
    stop: null,
    visible: false,
    lastPaint: 0,
    dpr: 1,
    // Tier changes are stamped onto the sparkline: a downgrade is the single most useful thing
    // to be able to correlate against a frame-time cliff, and it is invisible in the numbers.
    markers: [],
    unsubscribeTier: null
};

function palette() {
    // Read from the design tokens rather than restating hexes — the HUD floats over both themes
    // and a hardcoded panel colour is exactly the mistake app.css warns about.
    const root = getComputedStyle(document.documentElement);
    const token = (name, fallback) => (root.getPropertyValue(name) || '').trim() || fallback;
    return {
        good: token('--color-success-ink', '#15803d'),
        warn: token('--color-accent-ink', '#a16207'),
        bad: token('--color-danger', '#dc2626'),
        text: '#e8e8ef',
        dim: '#9aa0b4',
        panel: 'rgba(16, 16, 24, 0.86)'
    };
}

function ensureCanvas() {
    if (state.canvas) return state.canvas;

    const canvas = document.createElement('canvas');
    canvas.id = 'posee-perf-hud';
    // Decoration over the real page: never focusable, never in the a11y tree, never intercepting
    // a tap. Same rule as every other overlay canvas in this app.
    canvas.setAttribute('aria-hidden', 'true');
    canvas.style.cssText = [
        'position:fixed',
        'top:calc(env(safe-area-inset-top, 0px) + 8px)',
        'right:8px',
        `width:${WIDTH}px`,
        `height:${HEIGHT}px`,
        'pointer-events:none',
        'z-index:2147483000',
        'border-radius:8px',
        'box-shadow:0 4px 20px rgba(0,0,0,0.45)'
    ].join(';');

    state.dpr = Math.min(window.devicePixelRatio || 1, 2);
    canvas.width = Math.floor(WIDTH * state.dpr);
    canvas.height = Math.floor(HEIGHT * state.dpr);

    const ctx = canvas.getContext('2d');
    if (!ctx) return null;
    ctx.scale(state.dpr, state.dpr);

    state.canvas = canvas;
    state.ctx = ctx;
    return canvas;
}

function colourFor(value, warn, bad, colours) {
    if (value === null || value === undefined) return colours.dim;
    return value >= bad ? colours.bad : value >= warn ? colours.warn : colours.good;
}

function fmt(value, digits = 0, suffix = '') {
    if (value === null || value === undefined || !Number.isFinite(value)) return '—';
    return value.toFixed(digits) + suffix;
}

function drawSparkline(ctx, history, x, y, width, height, colours) {
    const max = Math.max(BUDGET_MS * 2, ...history);

    // Budget line first, so the trace draws over it.
    const budgetY = y + height - (BUDGET_MS / max) * height;
    ctx.strokeStyle = colours.warn;
    ctx.globalAlpha = 0.5;
    ctx.setLineDash([2, 2]);
    ctx.beginPath();
    ctx.moveTo(x, budgetY);
    ctx.lineTo(x + width, budgetY);
    ctx.stroke();
    ctx.setLineDash([]);
    ctx.globalAlpha = 1;

    const step = width / history.length;
    ctx.beginPath();
    for (let i = 0; i < history.length; i++) {
        const value = history[i];
        const pointY = y + height - Math.min(1, value / max) * height;
        if (i === 0) ctx.moveTo(x, pointY);
        else ctx.lineTo(x + i * step, pointY);
    }
    ctx.strokeStyle = colours.text;
    ctx.lineWidth = 1;
    ctx.stroke();

    // Tier-change markers, positioned by how many frames ago they happened.
    const now = performance.now();
    for (const marker of state.markers) {
        const age = now - marker.at;
        if (age > 4000) continue;
        const markerX = x + width - (age / 4000) * width;
        ctx.strokeStyle = colours.bad;
        ctx.globalAlpha = 0.8;
        ctx.beginPath();
        ctx.moveTo(markerX, y);
        ctx.lineTo(markerX, y + height);
        ctx.stroke();
        ctx.globalAlpha = 1;
    }
}

function paint() {
    const ctx = state.ctx;
    if (!ctx) return;

    const s = gfx.stats();
    const colours = palette();
    const latency = audio.latency();

    ctx.clearRect(0, 0, WIDTH, HEIGHT);
    ctx.fillStyle = colours.panel;
    ctx.fillRect(0, 0, WIDTH, HEIGHT);

    ctx.font = '600 22px ui-monospace, SFMono-Regular, Menlo, monospace';
    ctx.textBaseline = 'top';
    ctx.fillStyle = colourFor(60 - s.fps, 8, 20, colours);
    ctx.fillText(fmt(s.fps, 0), 8, 6);

    ctx.font = '10px ui-monospace, SFMono-Regular, Menlo, monospace';
    ctx.fillStyle = colours.dim;
    ctx.fillText('FPS', 52, 17);

    ctx.fillStyle = colours.text;
    ctx.fillText(`${s.tier}${s.autoDowngraded ? ' ↓auto' : ''}`, 92, 8);
    ctx.fillStyle = colours.dim;
    ctx.fillText(`${s.activeTasks} fx · ${s.glContexts} ctx`, 92, 20);

    drawSparkline(ctx, gfx.frameHistory(), 8, 34, WIDTH - 16, SPARK_HEIGHT, colours);

    // Two columns of labelled numbers. Anything the platform does not expose reports an em dash
    // rather than a zero — an absent measurement is not a measurement of nothing.
    const rows = [
        ['frame', fmt(s.frameMs, 1, 'ms'), colourFor(s.frameMs, BUDGET_MS, 33, colours)],
        ['cpu fx', fmt(s.cpuMs, 1, 'ms'), colourFor(s.cpuMs, 8, 16, colours)],
        ['gpu', s.gpuSupported ? fmt(s.gpuMs, 1, 'ms') : 'n/a', colourFor(s.gpuMs, 8, 16, colours)],
        ['worst', fmt(s.worstFrameMs, 0, 'ms'), colourFor(s.worstFrameMs, 33, 60, colours)],
        ['heap', fmt(s.heapMb, 0, 'mb'), colourFor(s.heapMb, 220, 400, colours)],
        ['long', `${s.longTasks}`, colourFor(s.worstLongTaskMs, 100, 250, colours)],
        ['inp', fmt(s.inpMs, 0, 'ms'), colourFor(s.inpMs, 200, 500, colours)],
        ['cls', fmt(s.layoutShift, 3), colourFor(s.layoutShift, 0.1, 0.25, colours)]
    ];

    const top = 34 + SPARK_HEIGHT + 8;
    rows.forEach(([label, value, colour], index) => {
        const column = index % 2;
        const row = Math.floor(index / 2);
        const x = 8 + column * ((WIDTH - 16) / 2);
        const y = top + row * 14;

        ctx.fillStyle = colours.dim;
        ctx.fillText(label, x, y);
        ctx.fillStyle = colour;
        ctx.textAlign = 'right';
        ctx.fillText(value, x + (WIDTH - 16) / 2 - 8, y);
        ctx.textAlign = 'left';
    });

    ctx.fillStyle = colours.dim;
    const audioLine = latency
        ? `audio ${latency.contextState} ${fmt(latency.outputMs, 0, 'ms')}`
        : 'audio idle';
    ctx.fillText(audioLine, 8, HEIGHT - 14);
}

export function isVisible() {
    return state.visible;
}

export function show() {
    if (state.visible) return true;

    const canvas = ensureCanvas();
    if (!canvas) return false;

    document.body.appendChild(canvas);
    state.visible = true;

    state.unsubscribeTier = gfx.onTierChanged(() => {
        state.markers.push({ at: performance.now() });
        if (state.markers.length > 16) state.markers.shift();
    });

    // Registered as a normal effect task, so the HUD appears in its own "active fx" count and
    // its cost lands in the same frame budget as everything else. An instrument that excludes
    // itself from its own measurement is lying by omission.
    state.stop = gfx.addTask('perf-hud', (now) => {
        if (now - state.lastPaint < REPAINT_MS) return;
        state.lastPaint = now;
        paint();
    });

    paint();
    return true;
}

export function hide() {
    if (!state.visible) return;

    state.stop?.();
    state.stop = null;
    state.unsubscribeTier?.();
    state.unsubscribeTier = null;
    state.canvas?.remove();
    state.visible = false;
    state.markers = [];
}

export function toggle() {
    if (state.visible) {
        hide();
        return false;
    }
    return show();
}

export function initPerfHud() {
    try {
        if (new URLSearchParams(window.location.search).get('fx') === 'debug') {
            show();
        }
    } catch {
        // Malformed query string is not a reason to fail module init.
    }

    window.addEventListener('keydown', (event) => {
        // Ctrl+Shift+F. Not a bare function key: those collide with browser and screen-reader
        // bindings, and this has to be safe to leave wired up in production.
        if (event.ctrlKey && event.shiftKey && (event.key === 'F' || event.key === 'f')) {
            event.preventDefault();
            toggle();
        }
    });
}
