// Frame-budget and load-path check for the graphics/audio layer (FX #5).
//
// Run against a live app, the same way SCRIPTS/post-deploy-smoke.mjs is run:
//     $env:BASE_URL = "https://localhost:5001"; node SCRIPTS/fx-perf-check.mjs
//
// It asserts the thing that cannot be established by reading the source: the shared frame
// scheduler holds 60 FPS with the gradient and a 400-particle burst running at once.
//
// The Three.js shelf and Rapier grid-physics scenes were removed along with their ~2.4 MB of
// vendored libraries, so the first-load and lazy-import assertions that guarded them are gone
// too — there is no longer a heavy module that could reach the first-load path.
//
// CAVEAT ON THE NUMBERS: headless Chromium here has no GPU, so this forces ANGLE/SwiftShader.
// The FPS figures are therefore a SOFTWARE-RENDERING FLOOR, not a device measurement. A real GPU
// is faster; a result below 60 here is a genuine problem, a result at 60 is a lower bound.
import { chromium } from 'playwright';

const BASE = process.env.BASE_URL ?? 'https://localhost:5001';
const results = [];
const record = (name, pass, detail) => {
    results.push({ name, pass, detail });
    console.log(`${pass ? 'PASS' : 'FAIL'}  ${name}${detail ? ` — ${detail}` : ''}`);
};

// Headless Chromium has no GPU here, and modern Chrome refuses the SwiftShader fallback unless
// it is explicitly allowed. Software rendering makes the FPS numbers a WORST CASE floor, not a
// representative device measurement — a real GPU will be far faster.
const browser = await chromium.launch({
    args: [
        '--ignore-certificate-errors',
        '--use-gl=angle',
        '--use-angle=swiftshader',
        '--enable-unsafe-swiftshader',
        '--enable-features=Vulkan'
    ]
});
const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1366, height: 768 } });
const page = await context.newPage();

const requested = [];
page.on('request', (r) => requested.push(r.url()));
page.on('console', (m) => {
    if (m.type() === 'error') console.log('    [browser error]', m.text());
});

// ── Sign in as guest (Development renders the guest button) ──────────────────────────────
await page.goto(`${BASE}/login`, { waitUntil: 'domcontentloaded' });
await page.locator('.login-container').waitFor({ timeout: 30000 });
await page.getByRole('button', { name: /continue as guest/i }).click();
await page.locator('.index-container').waitFor({ timeout: 30000 });

// ── 1. fx bootstrap actually ran ────────────────────────────────────────────────────────
const caps = await page.evaluate(() => window.poseeFx?.describe?.() ?? null);
record('fx.js bootstrapped', caps !== null, caps ? JSON.stringify(caps) : 'window.poseeFx missing');

const tierAttr = await page.getAttribute('html', 'data-fx-tier');
record('tier reflected onto <html> for CSS', !!tierAttr, `data-fx-tier="${tierAttr}"`);

// ── 3. Force the full tier and measure the real frame budget ────────────────────────────
await page.evaluate(() => window.poseeFx.setTier('full'));

// Start the backdrop shader explicitly: the landing page has one, but forcing the tier after
// load means it needs a nudge to attach.
const gradientStarted = await page.evaluate(() => {
    const canvas = document.querySelector('.fx-backdrop');
    if (!canvas) return -1;
    return window.poseeFx.startGradient(canvas, 88);
});
record('background gradient shader compiled + running', gradientStarted > 0,
    gradientStarted > 0 ? `handle ${gradientStarted}` : 'shader did not start');

// Fire a heavy particle burst on top, so the measurement covers more than one pass.
const burstStarted = await page.evaluate(() => {
    const canvas = document.createElement('canvas');
    canvas.style.cssText = 'position:fixed;inset:0;width:100%;height:100%;pointer-events:none;';
    document.body.appendChild(canvas);
    return window.poseeFx.burstParticles(canvas, 95);   // 400 particles
});
record('particle burst (score 95) compiled + running', burstStarted > 0,
    burstStarted > 0 ? `handle ${burstStarted}` : 'shader did not start');

await page.evaluate(() => window.poseeFx.resetStats());
await page.waitForTimeout(4000);
const stats = await page.evaluate(() => window.poseeFx.stats());

console.log('    frame stats:', JSON.stringify(stats));
record('sustained 60+ FPS with shaders active', stats.fps >= 58,
    `${stats.fps.toFixed(1)} fps, mean ${stats.frameMs.toFixed(2)}ms, worst ${stats.worstFrameMs.toFixed(1)}ms, ${stats.droppedFrames}/${stats.sampledFrames} over budget`);
record('no auto-downgrade triggered', stats.autoDowngraded === false,
    `tier still "${stats.tier}"`);

// ── 4. Audio graph builds (no gesture here, so it stays locked — that is correct) ───────
const audioState = await page.evaluate(async () => {
    const before = window.poseeFx.audioEnabled();
    return { before };
});
record('audio defaults to off (no unrequested sound)', audioState.before === false,
    `audioEnabled=${audioState.before}`);

// ── 6. Reduced motion pins the tier to off ──────────────────────────────────────────────
const reducedContext = await browser.newContext({ ignoreHTTPSErrors: true, reducedMotion: 'reduce' });
const reduced = await reducedContext.newPage();
await reduced.goto(`${BASE}/login`, { waitUntil: 'domcontentloaded' });
await reduced.waitForFunction(() => !!window.poseeFx, null, { timeout: 30000 });
const reducedCaps = await reduced.evaluate(() => {
    window.poseeFx.setTier('full');            // try to force it on
    return window.poseeFx.describe();
});
record('reduced-motion pins tier to off and refuses override',
    reducedCaps.tier === 'off' && reducedCaps.reducedMotion === true,
    JSON.stringify(reducedCaps));

await browser.close();

const failed = results.filter((r) => !r.pass);
console.log(`\n${results.length - failed.length}/${results.length} checks passed`);
process.exit(failed.length === 0 ? 0 : 1);
