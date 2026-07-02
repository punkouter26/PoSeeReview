// filepath: SCRIPTS/webapp-smoke.js
// Webapp-testing skill smoke run against PoSeeReview.Api on https://localhost:5001.
// Verifies that:
//   1. Root page (Blazor WASM) renders without an unhandled exception
//   2. The "Something Went Wrong" / API_KEY_INVALID banner is NOT present
//   3. /api/restaurants/nearby returns 200 with real Google Places data
//   4. No severe console errors fire during initial render
//
// Usage: node SCRIPTS/webapp-smoke.js

const { chromium } = require('playwright');

const APP_URL = process.env.APP_URL || 'https://localhost:5001/';
const NEARBY_URL = 'https://localhost:5001/api/restaurants/nearby?latitude=40.7128&longitude=-74.0060&radius=1500';

(async () => {
  const browser = await chromium.launch({ headless: true });
  const context = await browser.newContext({ ignoreHTTPSErrors: true });
  const page = await context.newPage();

  const consoleErrors = [];
  page.on('console', msg => {
    if (msg.type() === 'error') {
      consoleErrors.push(msg.text());
    }
  });
  page.on('pageerror', err => {
    consoleErrors.push(`pageerror: ${err.message}`);
  });

  const results = [];

  // 0. Authenticate via Dev-only /auth/login/fake (NET_RULES 4.4) so the
  //    forced-auth gate doesn't bounce us to the login page.
  console.log(`[0/4] Signing in via /auth/login/fake`);
  const fakeLogin = await page.request.get('https://localhost:5001/auth/login/fake?returnUrl=/', { maxRedirects: 0 });
  if (![200, 302, 303].includes(fakeLogin.status())) {
    throw new Error(`/auth/login/fake returned ${fakeLogin.status()}`);
  }

  // 1. Root page renders.
  console.log(`[1/4] Navigating to ${APP_URL}`);
  await page.goto(APP_URL, { waitUntil: 'networkidle', timeout: 30000 });
  const title = await page.title();
  // After sign-in the title should be 'PoSeeReview' (home page).
  results.push({ check: 'page title', value: title, pass: title === 'PoSeeReview' });

  // 2. No "API_KEY_INVALID" / "Something Went Wrong" banner.
  const bodyText = await page.locator('body').innerText();
  const hasApiKeyInvalid = /API_KEY_INVALID|API key not valid/i.test(bodyText);
  const hasSomethingWrong = /Something Went Wrong/i.test(bodyText);
  results.push({ check: 'no API_KEY_INVALID banner', pass: !hasApiKeyInvalid });
  results.push({ check: 'no "Something Went Wrong" banner', pass: !hasSomethingWrong });

  // Screenshot for visual verification.
  const shotPath = `poseereview-home-${Date.now()}.png`;
  await page.screenshot({ path: shotPath, fullPage: true });
  console.log(`    screenshot saved: ${shotPath}`);

  // 3. Nearby endpoint returns 200 with real data.
  console.log(`[3/4] Fetching ${NEARBY_URL}`);
  const apiResponse = await page.request.get(NEARBY_URL);
  const apiStatus = apiResponse.status();
  const apiJson = await apiResponse.json();
  const restaurantCount = apiJson?.restaurants?.length ?? 0;
  const firstName = apiJson?.restaurants?.[0]?.name ?? '(none)';
  results.push({ check: 'nearby 200 OK', pass: apiStatus === 200, value: apiStatus });
  results.push({
    check: 'nearby has restaurants',
    pass: restaurantCount >= 1,
    value: `${restaurantCount} restaurants, first: ${firstName}`,
  });

  // 4. Console error budget.
  results.push({
    check: 'console errors',
    pass: consoleErrors.length === 0,
    value: consoleErrors.length === 0 ? 'none' : consoleErrors.join(' | '),
  });

  await browser.close();

  // Report
  console.log('\n=== webapp-testing smoke results ===');
  let allPass = true;
  for (const r of results) {
    const status = r.pass ? 'PASS' : 'FAIL';
    if (!r.pass) allPass = false;
    const valueStr = r.value !== undefined ? ` (${JSON.stringify(r.value)})` : '';
    console.log(`  [${status}] ${r.check}${valueStr}`);
  }
  console.log(allPass ? '\nALL CHECKS PASSED' : '\nFAILURES DETECTED');
  process.exit(allPass ? 0 : 1);
})().catch(err => {
  console.error('FATAL:', err);
  process.exit(2);
});