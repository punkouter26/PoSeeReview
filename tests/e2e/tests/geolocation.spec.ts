import { test, expect, Page } from '@playwright/test';

/**
 * End-to-end tests for restaurant list functionality
 * Tests the actual UI flow: page loads -> enable location -> restaurants display -> user clicks restaurant
 */

const BASE_URL = 'https://localhost:5001';

// Capture JS errors for all tests and fail immediately if Blazor fails to boot
const jsErrors: string[] = [];

async function waitForHomeReady(page: Page): Promise<void> {
  await page.goto(BASE_URL, { waitUntil: 'domcontentloaded' });
  await expect(page.locator('body')).toBeVisible({ timeout: 15000 });
  await page.waitForFunction(() => document.readyState === 'complete');
}

async function enableLocationAndWaitForRestaurants(page: Page): Promise<void> {
  const enableButton = page.getByRole('button', { name: 'Use My Location' });

  // Some flows may already have location enabled from previous state.
  if (await enableButton.isVisible({ timeout: 4000 }).catch(() => false)) {
    await enableButton.click();
  }

  const restaurantCards = page.locator('.restaurant-card');
  await expect(restaurantCards.first()).toBeVisible({ timeout: 25000 });
}

test.beforeEach(async ({ context, page }) => {
  jsErrors.length = 0;
  page.on('pageerror', (err) => jsErrors.push(err.message));
  // Grant geolocation permission and set mock location (Seattle)
  await context.grantPermissions(['geolocation']);
  await context.setGeolocation({
    latitude: 47.6062,
    longitude: -122.3321
  });
  
  await waitForHomeReady(page);
});

test.afterEach(async () => {
  const fatal = jsErrors.filter(e => e.includes('Failed to start platform') || e.includes('failed 404'));
  if (fatal.length > 0) {
    throw new Error(`Blazor WASM boot failed — JS errors detected:\n${fatal.slice(0, 3).join('\n')}`);
  }
});

test.describe('Restaurant List Tests', () => {
  
  test('HomePage: On load, displays nearby restaurants after enabling location', async ({ page }) => {
    await enableLocationAndWaitForRestaurants(page);

    const restaurantCards = page.locator('.restaurant-card');

    // Assert - Verify at least one restaurant is displayed
    const count = await restaurantCards.count();
    expect(count).toBeGreaterThan(0);
  });

  test('HomePage: Shows restaurant details', async ({ page }) => {
    await enableLocationAndWaitForRestaurants(page);

    const restaurantCards = page.locator('.restaurant-card');

    // Assert - Verify at least one restaurant has required elements
    const firstCard = restaurantCards.first();
    await expect(firstCard.locator('h3, h4, .restaurant-name')).toBeVisible();
    await expect(firstCard.locator('.address, address')).toBeVisible();
    await expect(firstCard.locator('.rating, .stars').first()).toBeVisible();
  });

  test('RestaurantCard: When clicked, navigates to details page', async ({ page }) => {
    await enableLocationAndWaitForRestaurants(page);

    const restaurantCards = page.locator('.restaurant-card');

    // Act - Click the first restaurant card
    await restaurantCards.first().click();

    // Assert - Wait for navigation or content change
    await page.waitForURL('**/comic/**', { timeout: 15000 });
    
    // Check if we navigated to a comic page
    const url = page.url();
    expect(url).toContain('/comic/');
  });

  test('GeolocationAPI: Verifies coordinates are sent to backend', async ({ page }) => {
    // Arrange - Set up request interception to verify coordinates
    let apiCallMade = false;
    
    page.on('request', request => {
      if (request.url().includes('/api/restaurants') && 
          request.url().includes('latitude') && 
          request.url().includes('longitude')) {
        apiCallMade = true;
      }
    });

    await enableLocationAndWaitForRestaurants(page);

    // Assert - Verify the API was called with coordinates
    expect(apiCallMade).toBeTruthy();
  });

  test('GeolocationService: Verifies JavaScript interop works', async ({ page }) => {
    await waitForHomeReady(page);

    // Act - Evaluate that the geolocation JavaScript object and function exist
    const hasGeolocationFunction = await page.evaluate(() => {
      return typeof (window as any).geolocation === 'object' && 
             typeof (window as any).geolocation.getCurrentPosition === 'function';
    });

    // Assert
    expect(hasGeolocationFunction).toBeTruthy();
  });

  test('RestaurantList: Displays distance from user location', async ({ page }) => {
    // Wait for the location prompt to appear
    const enableButton = page.getByRole('button', { name: 'Use My Location' });
    await expect(enableButton).toBeVisible({ timeout: 10000 });
    
    await enableButton.click();
    
    // Wait for restaurants to load
    const restaurantCards = page.locator('.restaurant-card');
    await expect(restaurantCards.first()).toBeVisible({ timeout: 15000 });

    // Assert - Verify distance is displayed with proper units
    const distanceElement = page.locator('.distance').first();
    await expect(distanceElement).toBeVisible();
    
    const distanceText = await distanceElement.textContent();
    expect(distanceText).toMatch(/\d+.*(km|mi|away|m)/);
  });

  test('RestaurantList: Displays review count', async ({ page }) => {
    // Wait for the location prompt to appear
    const enableButton = page.getByRole('button', { name: 'Use My Location' });
    await expect(enableButton).toBeVisible({ timeout: 10000 });
    
    await enableButton.click();
    
    // Wait for restaurants to load
    const restaurantCards = page.locator('.restaurant-card');
    await expect(restaurantCards.first()).toBeVisible({ timeout: 15000 });

    // Assert - Verify review count is displayed
    const reviewCountElement = page.locator('.review-count').first();
    await expect(reviewCountElement).toBeVisible();
    
    const reviewText = await reviewCountElement.textContent();
    expect(reviewText).toMatch(/\d+/);
  });
});
