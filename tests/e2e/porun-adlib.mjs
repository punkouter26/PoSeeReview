/**
 * PoRun Ad-hoc Discovery Test
 * Captures screenshots + tests key UI interactions without using the existing E2E suite.
 */
import { chromium } from '@playwright/test';
import { mkdir } from 'fs/promises';
import path from 'path';

const BASE_URL = 'https://localhost:5001';
const SHOTS_DIR = path.resolve('../../docs/screenshots');
const WAIT = 3500;

await mkdir(SHOTS_DIR, { recursive: true });

const browser = await chromium.launch({ headless: true });
const ctx = await browser.newContext({
  ignoreHTTPSErrors: true,
  viewport: { width: 1280, height: 800 },
});
const page = await ctx.newPage();

const results = [];

async function shot(name, description) {
  const p = path.join(SHOTS_DIR, `${name}.png`);
  await page.screenshot({ path: p, fullPage: false });
  console.log(`📸  ${name}: ${description}`);
  results.push({ name, description });
}

async function check(label, fn) {
  try {
    await fn();
    console.log(`  ✅  ${label}`);
    return true;
  } catch (e) {
    console.log(`  ❌  ${label} — ${e.message.split('\n')[0]}`);
    return false;
  }
}

// ─── 1. HOME PAGE ──────────────────────────────────────────────────────────
console.log('\n=== HOME PAGE ===');
await page.goto(BASE_URL, { waitUntil: 'networkidle' });
await page.waitForTimeout(WAIT);
await shot('09-home-final', 'Home page on load');

// Check hero banner elements
await check('Hero h1 visible', () => page.waitForSelector('.app-header h1', { timeout: 3000 }));
await check('Search field visible', () => page.waitForSelector('fluent-text-field, input[type="text"]', { timeout: 3000 }));
await check('Leaderboard hero link visible', () => page.waitForSelector('.leaderboard-hero-link', { timeout: 3000 }));
await check('Location prompt visible', () => page.waitForSelector('.location-prompt', { timeout: 3000 }));

// Scroll down to check restaurant grid area
await page.evaluate(() => window.scrollTo(0, 400));
await page.waitForTimeout(500);

// ─── 2. MOBILE VIEW ────────────────────────────────────────────────────────
console.log('\n=== MOBILE VIEW ===');
await page.setViewportSize({ width: 390, height: 844 });
await page.reload({ waitUntil: 'networkidle' });
await page.waitForTimeout(WAIT);
await shot('03-home-mobile', 'Home page on iPhone 14 viewport');

// Test hamburger menu toggle
await check('Hamburger button exists', () => page.waitForSelector('.navbar-toggler', { timeout: 3000 }));
const hamburger = page.locator('.navbar-toggler');
if (await hamburger.count() > 0) {
  await hamburger.click();
  await page.waitForTimeout(600);
  await shot('04-nav-open-mobile', 'Nav menu open on mobile (backdrop test)');
  await check('Nav backdrop rendered', () => page.waitForSelector('.nav-backdrop', { timeout: 2000 }));

  // Click backdrop to close
  const backdrop = page.locator('.nav-backdrop');
  if (await backdrop.count() > 0) {
    await backdrop.click();
    await page.waitForTimeout(400);
    await check('Nav closes after backdrop click', async () => {
      const cnt = await page.locator('.nav-backdrop').count();
      if (cnt > 0) throw new Error('Backdrop still visible');
    });
  }
}

// ─── 3. LOGIN PAGE ─────────────────────────────────────────────────────────
console.log('\n=== LOGIN PAGE ===');
await page.setViewportSize({ width: 1280, height: 800 });
await page.goto(`${BASE_URL}/login`, { waitUntil: 'networkidle' });
await page.waitForTimeout(WAIT);
await shot('05-login', 'Login page');

await check('Login logo visible', () => page.waitForSelector('.login-logo', { timeout: 3000 }));
await check('Microsoft sign-in button', () => page.waitForSelector('.login-btn-microsoft', { timeout: 3000 }));
await check('Guest/anon button', () => page.waitForSelector('.login-btn-secondary', { timeout: 3000 }));

// ─── 4. LEADERBOARD ────────────────────────────────────────────────────────
console.log('\n=== LEADERBOARD ===');
// Click guest to navigate
const anonBtn = page.locator('.login-btn-secondary');
if (await anonBtn.count() > 0) {
  await anonBtn.click();
  await page.waitForTimeout(2000);
}
await page.goto(`${BASE_URL}/leaderboard`, { waitUntil: 'networkidle' });
await page.waitForTimeout(WAIT);
await shot('06-leaderboard', 'Leaderboard page');

await check('Region chips exist', () => page.waitForSelector('.region-chips', { timeout: 4000 }));
await check('At least one chip rendered', async () => {
  const n = await page.locator('.region-chip').count();
  if (n < 1) throw new Error(`Only ${n} chips found`);
  console.log(`     (${n} region chips found)`);
});

// Click a region chip
const chips = page.locator('.region-chip');
const chipCount = await chips.count();
if (chipCount > 1) {
  await chips.nth(1).click();
  await page.waitForTimeout(1500);
  await shot('07-leaderboard-region-chip', 'Leaderboard after clicking region chip');
  await check('Chip becomes active', async () => {
    const active = await page.locator('.region-chip.active').count();
    if (active < 1) throw new Error('No chip has active class');
  });
}

// ─── 5. HOME SEARCH FLOW ───────────────────────────────────────────────────
console.log('\n=== HOME SEARCH FLOW ===');
await page.goto(BASE_URL, { waitUntil: 'networkidle' });
await page.waitForTimeout(WAIT);

// Try searching by typing in the search field
const searchInput = page.locator('fluent-text-field input, input[placeholder*="location"], input[placeholder*="Last"], input[placeholder*="city"]').first();
if (await searchInput.count() > 0) {
  await searchInput.fill('New York');
  await page.waitForTimeout(300);
  await check('Search field accepts input', async () => {
    const val = await searchInput.inputValue();
    if (!val.includes('New York')) throw new Error(`Got: ${val}`);
  });
  // Press Enter or click search button
  const searchBtn = page.locator('button:has-text("Search"), fluent-button:has-text("Search")').first();
  if (await searchBtn.count() > 0) {
    await searchBtn.click();
  } else {
    await searchInput.press('Enter');
  }
  await page.waitForTimeout(4000);
  await shot('08-search-results', 'Search results for New York');

  // Check if restaurant cards appeared
  await check('Restaurant cards rendered', async () => {
    const n = await page.locator('.restaurant-card, [class*="restaurant"]').count();
    console.log(`     (${n} restaurant elements found)`);
    if (n < 1) throw new Error('No restaurant cards found');
  });
}

// ─── 6. RESTAURANT CARD TIER COLORS ────────────────────────────────────────
console.log('\n=== RESTAURANT CARDS ===');
await check('Tier classes applied', async () => {
  const high = await page.locator('.tier-high').count();
  const mid  = await page.locator('.tier-mid').count();
  const low  = await page.locator('.tier-low').count();
  console.log(`     tier-high:${high}  tier-mid:${mid}  tier-low:${low}`);
  if (high + mid + low < 1) throw new Error('No tier classes found on cards');
});
await check('Rating bars rendered', async () => {
  const n = await page.locator('.rating-bar').count();
  console.log(`     (${n} rating bars found)`);
  if (n < 1) throw new Error('No rating bars');
});

// ─── 7. ACCESSIBILITY ──────────────────────────────────────────────────────
console.log('\n=== ACCESSIBILITY SPOT CHECK ===');
await check('Page title set', async () => {
  const title = await page.title();
  console.log(`     title: "${title}"`);
  if (!title) throw new Error('No page title');
});
await check('html lang attribute', async () => {
  const lang = await page.evaluate(() => document.documentElement.lang);
  console.log(`     lang="${lang}"`);
  if (!lang) throw new Error('lang attr missing');
});

// Check for images without alt text
const imgsMissingAlt = await page.evaluate(() =>
  Array.from(document.querySelectorAll('img:not([alt])')).map(i => i.src.split('/').pop())
);
if (imgsMissingAlt.length > 0) {
  console.log(`  ⚠️  Images missing alt text: ${imgsMissingAlt.slice(0, 5).join(', ')}`);
} else {
  console.log(`  ✅  All visible images have alt text`);
}

// ─── 8. CONSOLE ERRORS ─────────────────────────────────────────────────────
const consoleErrors = [];
page.on('console', msg => { if (msg.type() === 'error') consoleErrors.push(msg.text()); });
page.on('pageerror', err => consoleErrors.push(err.message));

await page.goto(BASE_URL, { waitUntil: 'networkidle' });
await page.waitForTimeout(2000);
if (consoleErrors.length > 0) {
  console.log(`\n  ⚠️  Browser console errors (${consoleErrors.length}):`);
  consoleErrors.slice(0, 5).forEach(e => console.log(`     - ${e.substring(0, 120)}`));
} else {
  console.log('\n  ✅  No browser console errors');
}

await browser.close();

console.log(`\n✅  Done — ${results.length} screenshots saved to: ${SHOTS_DIR}`);
