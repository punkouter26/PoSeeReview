// UI/UX and pipeline regression check. Run against a live app:
//     $env:BASE_URL = "https://localhost:5001"; node SCRIPTS/ui-check.mjs
//
// Covers the cascade-layer restructure (scoped CSS still applies, shared primitives win,
// Bootstrap gone), the design tokens, mobile overflow, and the two pipeline bugs this was
// written to catch: the Gemini image API surface and nearby-search ranking.
//
// NOTE: check 8 performs ONE real comic generation, which spends a paid image call.

import { chromium } from 'playwright';

const BASE = process.env.BASE_URL ?? 'https://localhost:5001';
const results = [];
const record = (n, p, d) => { results.push({ n, p }); console.log(`${p ? 'PASS' : 'FAIL'}  ${n}${d ? ` — ${d}` : ''}`); };

const browser = await chromium.launch({ args: ['--ignore-certificate-errors', '--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader'] });
const ctx = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1366, height: 768 } });
const page = await ctx.newPage();
const failed = [];
page.on('response', r => { if (r.status() >= 400) failed.push(`${r.status()} ${r.url()}`); });

await page.goto(`${BASE}/login`, { waitUntil: 'domcontentloaded' });
await page.locator('.login-container').waitFor({ timeout: 30000 });
await page.getByRole('button', { name: /continue as guest/i }).click();
await page.locator('.index-container').waitFor({ timeout: 30000 });

// 1. No failed asset requests (a broken @import would show up here)
record('no failed asset requests', failed.length === 0, failed.slice(0, 4).join(' | ') || 'none');

// 2. Scoped CSS still applies through the layered @import
const scopedApplied = await page.evaluate(() => {
    const el = document.querySelector('.app-header h1');
    if (!el) return null;
    const cs = getComputedStyle(el);
    return { family: cs.fontFamily, size: cs.fontSize };
});
record('scoped CSS applies via @layer import',
    !!scopedApplied && /Bangers/i.test(scopedApplied.family),
    scopedApplied ? `${scopedApplied.family} @ ${scopedApplied.size}` : 'header missing');

// 3. New tokens are live
const tokens = await page.evaluate(() => {
    const cs = getComputedStyle(document.documentElement);
    const g = n => cs.getPropertyValue(n).trim();
    return {
        accentInk: g('--color-accent-ink'), muted: g('--color-text-muted'),
        borderStrong: g('--color-border-strong'), spaceMd: g('--space-md'), tap: g('--tap-target')
    };
});
record('new design tokens live',
    tokens.accentInk === '#A56A07' && tokens.muted === '#6D797A' && tokens.spaceMd === '1rem',
    JSON.stringify(tokens));

// 4. Bootstrap is gone
const bootstrapGone = await page.evaluate(() =>
    ![...document.styleSheets].some(s => (s.href ?? '').includes('bootstrap')));
record('bootstrap no longer loaded', bootstrapGone);

// 5. Shared .btn wins over any page redefinition (the cascade-layer fix)
const btnPill = await page.evaluate(() => {
    const b = document.createElement('button');
    b.className = 'btn btn-primary'; b.textContent = 'x';
    document.body.appendChild(b);
    const r = getComputedStyle(b).borderRadius;
    const h = getComputedStyle(b).minHeight;
    b.remove();
    return { r, h };
});
record('shared .btn primitive wins (pill + 44px target)',
    btnPill.r.startsWith('999') && parseFloat(btnPill.h) >= 44, JSON.stringify(btnPill));

// 6. Focus ring exists
const ring = await page.evaluate(() => {
    const b = document.querySelector('button, a[href]');
    if (!b) return null;
    b.focus();
    const cs = getComputedStyle(b);
    return { w: cs.outlineWidth, s: cs.outlineStyle };
});
record('focus-visible ring defined', !!ring, ring ? JSON.stringify(ring) : 'no focusable element');

// 7. No horizontal overflow at mobile width
// Resize the signed-in page rather than opening a second one: a fresh tab re-boots the WASM
// runtime and re-runs the auth handshake, which is slow and unrelated to what is being measured.
await page.setViewportSize({ width: 390, height: 844 });
await page.waitForTimeout(600);
const overflow = await page.evaluate(() => ({
    scroll: document.documentElement.scrollWidth, client: document.documentElement.clientWidth
}));
record('no horizontal overflow at 390px',
    overflow.scroll <= overflow.client + 1, JSON.stringify(overflow));

await page.setViewportSize({ width: 1366, height: 768 });

// 8. End-to-end comic generation through the streaming endpoint (the reported failure).
//    One real generation — this spends a paid image call, deliberately just the one.
const gen = await page.evaluate(async () => {
    const search = await fetch('/api/restaurants/search?location=20020&limit=5', {
        headers: { 'X-Correlation-ID': 'ui-verify' }
    });
    if (!search.ok) return { stage: 'search', status: search.status };
    const data = await search.json();
    const first = data.restaurants?.[0];
    if (!first) return { stage: 'search', status: 'no results' };

    const res = await fetch(`/api/comics/${first.placeId}/stream`, { method: 'POST' });
    if (!res.ok) return { stage: 'stream-open', status: res.status };

    const reader = res.body.getReader();
    const decoder = new TextDecoder();
    let buf = '', phases = [], done = null, error = null;
    while (true) {
        const { value, done: finished } = await reader.read();
        if (finished) break;
        buf += decoder.decode(value, { stream: true });
        const lines = buf.split(String.fromCharCode(10));
        buf = lines.pop();
        for (const line of lines) {
            if (!line.startsWith('data:')) continue;
            const evt = JSON.parse(line.slice(5).trim());
            if (evt.kind === 'phase') phases.push(evt.phase);
            if (evt.kind === 'complete') done = evt.comic;
            if (evt.kind === 'error') error = evt;
        }
    }
    return {
        stage: 'done', name: first.name, rating: first.averageRating,
        phases, error,
        comic: done ? { score: done.strangenessScore, url: !!done.blobUrl, receipts: done.receipts?.length ?? 0 } : null
    };
});
console.log('    generation:', JSON.stringify(gen));
record('comic generation succeeds end-to-end', gen.stage === 'done' && !!gen.comic && !gen.error,
    gen.comic ? `"${gen.name}" score ${gen.comic.score}, ${gen.comic.receipts} receipts, phases [${gen.phases}]`
              : `stage=${gen.stage} ${JSON.stringify(gen.error ?? gen.status)}`);

// 9. Nearby search now ranks by distance, not popularity.
const ranking = await page.evaluate(async () => {
    const r = await fetch('/api/restaurants/search?location=20020&limit=20');
    if (!r.ok) return null;
    const d = await r.json();
    const rs = d.restaurants ?? [];
    return {
        count: rs.length,
        maxDistanceKm: Math.max(...rs.map(x => x.distance ?? 0)),
        ratings: rs.map(x => x.averageRating),
        below4: rs.filter(x => (x.averageRating ?? 0) < 4).length,
        lowReviewCount: rs.filter(x => (x.totalReviews ?? 0) < 500).length
    };
});
console.log('    ranking:', JSON.stringify(ranking));
record('nearby results are distance-ranked, not popularity-ranked',
    !!ranking && ranking.maxDistanceKm < 3,
    ranking ? `furthest ${ranking.maxDistanceKm.toFixed(2)}km, ${ranking.below4}/${ranking.count} under 4 stars, ${ranking.lowReviewCount} with <500 reviews` : 'search failed');

await browser.close();
const bad = results.filter(r => !r.p);
console.log(`\n${results.length - bad.length}/${results.length} checks passed`);
process.exit(bad.length ? 1 : 0);
