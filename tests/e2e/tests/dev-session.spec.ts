import { test, expect } from '@playwright/test';

/**
 * E2E tests for the ANON login dev session flow.
 *
 * Prerequisites:
 * - App must be running in Development mode (dotnet run --project src/Po.SeeReview.Api)
 * - The ANON button section is only visible when the Blazor env is "Development"
 */

test.describe('Dev Session - ANON login flow @smoke', () => {
  test.beforeEach(async ({ page }) => {
    // Clear localStorage before each test to ensure clean state
    await page.goto('/');
    await page.evaluate(() => localStorage.clear());
  });

  test('ANON LOGIN button is visible on home page in dev mode @smoke', async ({ page }) => {
    await page.goto('/');
    // Wait for Blazor to hydrate and dev banner to render
    await page.locator('.dev-session-banner').waitFor({ state: 'visible', timeout: 15_000 });

    const anonButton = page.getByRole('button', { name: /anon login/i });
    await expect(anonButton).toBeVisible({ timeout: 10_000 });
  });

  test('clicking ANON LOGIN creates and displays an ANON identity @smoke', async ({ page }) => {
    await page.goto('/');
    await page.locator('.dev-session-banner').waitFor({ state: 'visible', timeout: 15_000 });

    const anonButton = page.getByRole('button', { name: /anon login/i });
    await anonButton.click();

    // Should display the generated ANON identity somewhere on the page
    const anonIdentity = page.locator('text=/ANON\\d{6}/');
    await expect(anonIdentity).toBeVisible({ timeout: 10_000 });
  });

  test('ANON session persists after page reload', async ({ page }) => {
    await page.goto('/');
    await page.locator('.dev-session-banner').waitFor({ state: 'visible', timeout: 15_000 });

    // Create a session
    const anonButton = page.getByRole('button', { name: /anon login/i });
    await anonButton.click();

    // Capture the displayed user id
    const anonIdentity = page.locator('text=/ANON\\d{6}/').first();
    await expect(anonIdentity).toBeVisible({ timeout: 10_000 });
    const userId = await anonIdentity.textContent();

    // Reload
    await page.reload();
    await page.locator('.dev-session-banner').waitFor({ state: 'visible', timeout: 15_000 });

    // Same session should still be shown (read from localStorage)
    const persistedIdentity = page.locator(`text=${userId}`);
    await expect(persistedIdentity).toBeVisible({ timeout: 10_000 });
  });

  test('ANON session id is stored in localStorage', async ({ page }) => {
    await page.goto('/');
    await page.locator('.dev-session-banner').waitFor({ state: 'visible', timeout: 15_000 });

    await page.getByRole('button', { name: /anon login/i }).click();

    // Wait for the session to appear in UI first
    await expect(page.locator('text=/ANON\\d{6}/')).toBeVisible({ timeout: 10_000 });

    const storedSession = await page.evaluate(() => {
      return localStorage.getItem('posee_dev_session');
    });

    expect(storedSession).not.toBeNull();
    const parsed = JSON.parse(storedSession!);
    expect(parsed.userId).toMatch(/^ANON\d{6}$/);
  });

  test('new ANON LOGIN click produces a different identity than the previous one', async ({ page }) => {
    await page.goto('/');
    await page.locator('.dev-session-banner').waitFor({ state: 'visible', timeout: 15_000 });

    const button = page.getByRole('button', { name: /anon login/i });

    // First click
    await button.click();
    const first = await page.locator('text=/ANON\\d{6}/').first().textContent({ timeout: 10_000 });

    // Clear and click again
    await page.evaluate(() => localStorage.clear());
    await button.click();
    const second = await page.locator('text=/ANON\\d{6}/').first().textContent({ timeout: 10_000 });

    expect(first).not.toEqual(second);
  });

  test('Diagnostics nav link is visible in dev mode', async ({ page }) => {
    await page.goto('/');
    await page.locator('.dev-session-banner').waitFor({ state: 'visible', timeout: 15_000 });

    const diagLink = page.getByRole('link', { name: /diagnostics/i });
    await expect(diagLink).toBeVisible({ timeout: 10_000 });
  });

  test('Diagnostics page loads and shows snapshot data', async ({ page }) => {
    await page.goto('/diagnostics');
    // Wait for diagnostics content to render
    await page.locator('.diagnostics-container').waitFor({ state: 'visible', timeout: 15_000 });

    // Should show environment info
    const envText = page.locator('text=/Development|Production|Staging/');
    await expect(envText).toBeVisible({ timeout: 15_000 });
  });
});
