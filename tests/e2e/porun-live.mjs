// Live browse of PoSeeReview running on http://localhost:5245
import { chromium } from '@playwright/test';
import { mkdir } from 'fs/promises';
import path from 'path';

const BASE_URL = 'http://localhost:5245';
const SHOTS = path.resolve('docs/screenshots');
await mkdir(SHOTS, { recursive: true });

const browser = await chromium.launch({ headless: true });
const ctx = await browser.newContext({
  viewport: { width: 1280, height: 800 },
  permissions: [],
});
const page = await ctx.newPage();

const consoleErrors = [];
const networkFailures = [];
page.on('console', m => { if (m.type() === 'error') consoleErrors.push(m.text()); });
page.on('pageerror', e => consoleErrors.push('PAGEERROR: ' + e.message));
page.on('requestfailed', r => networkFailures.push(`${r.method()} ${r.url()} :: ${r.failure()?.errorText}`));
page.on('response', r => { if (r.status() >= 400) networkFailures.push(`${r.status()} ${r.url()}`); });

async function shot(name) {
  const p = path.join(SHOTS, `${name}.png`);
  await page.screenshot({ path: p, fullPage: true });
  console.log(`📸  ${name}.png`);
}

console.log('\n=== HOME ===');
await page.goto(BASE_URL, { waitUntil: 'networkidle', timeout: 20000 }).catch(e => console.log('Nav err:', e.message));
await page.waitForTimeout(3000);
await shot('live-01-home');

const title = await page.title();
console.log(`title: ${title}`);

// Check for the AI Model selector
const aiModel = await page.locator('#ai-model-select').count();
console.log(`AI Model selector present: ${aiModel > 0}`);
if (aiModel > 0) {
  const opts = await page.locator('#ai-model-select option').allTextContents();
  console.log(`AI Model options: ${JSON.stringify(opts)}`);
  const selected = await page.locator('#ai-model-select').inputValue();
  console.log(`Default selected: ${selected}`);
}

// Check the location prompt + search
const promptH2 = await page.locator('.prompt-card h2').textContent().catch(() => null);
console.log(`Prompt h2: ${promptH2}`);
const searchInput = await page.locator('input[placeholder*="Seattle"], input[placeholder*="Last"]').count();
console.log(`Search input count: ${searchInput}`);

// Try searching
const inp = page.locator('input[placeholder*="Seattle"], input[placeholder*="Last"]').first();
if (await inp.count() > 0) {
  await inp.fill('Seattle');
  await page.waitForTimeout(300);
  const searchBtn = page.locator('button:has-text("Search")').first();
  if (await searchBtn.count() > 0 && await searchBtn.isEnabled().catch(() => false)) {
    await searchBtn.click();
    await page.waitForTimeout(5000);
    await shot('live-02-search-results');
  } else {
    await inp.press('Enter');
    await page.waitForTimeout(5000);
    await shot('live-02-search-results');
  }
}

console.log('\n=== LEADERBOARD ===');
await page.goto(`${BASE_URL}/leaderboard`, { waitUntil: 'networkidle', timeout: 15000 }).catch(e => console.log('Nav err:', e.message));
await page.waitForTimeout(3000);
await shot('live-03-leaderboard');

console.log('\n=== LOGIN ===');
await page.goto(`${BASE_URL}/login`, { waitUntil: 'networkidle', timeout: 15000 }).catch(e => console.log('Nav err:', e.message));
await page.waitForTimeout(2000);
await shot('live-04-login');
const anonBtn = await page.locator('button:has-text("Continue As Guest"), button:has-text("Guest")').count();
console.log(`Anon/Guest button visible (Dev env): ${anonBtn > 0}`);

console.log('\n=== DIAG (blazor) ===');
await page.goto(`${BASE_URL}/diag`, { waitUntil: 'networkidle', timeout: 15000 }).catch(e => console.log('Nav err:', e.message));
await page.waitForTimeout(3000);
await shot('live-05-diag');
const diagHeading = await page.locator('h1').first().textContent().catch(() => '');
console.log(`Diag h1: ${diagHeading}`);

console.log('\n=== NAV MENU — MOCK BADGE? ===');
await page.goto(BASE_URL, { waitUntil: 'networkidle', timeout: 15000 }).catch(()=>{});
await page.waitForTimeout(2000);
const mockBadge = await page.locator('.nav-mock-badge').count();
console.log(`Mock badge visible: ${mockBadge > 0}  (expected: false in Development)`);
await shot('live-06-nav-mockbadge');

console.log('\n=== /api/diag/mock-status from browser ===');
const mockResp = await page.evaluate(async () => {
  const r = await fetch('/api/diag/mock-status', { headers: { 'X-Requested-With': 'XMLHttpRequest' } });
  return { status: r.status, body: await r.text() };
});
console.log('mock-status:', JSON.stringify(mockResp));

console.log('\n=== NETWORK FAILURES ===');
for (const f of networkFailures.slice(0, 20)) console.log('  ', f);
console.log(`\n=== CONSOLE ERRORS (${consoleErrors.length}) ===`);
for (const e of consoleErrors.slice(0, 20)) console.log('  ', e.substring(0, 200));

await browser.close();
console.log('\n✅  Done — screenshots in', SHOTS);
