import { test, expect, Page, BrowserContext } from '@playwright/test';

/**
 * End-to-end tests for restaurant list functionality
 * Tests the actual UI flow: page loads -> enable location -> restaurants display -> user clicks restaurant
 */

const BASE_URL = 'http://localhost:5000';

// Setup geolocation for all tests in this file
test.beforeEach(async ({ context, page }) => {
  // Grant geolocation permission and set mock location (Seattle)
  await context.grantPermissions(['geolocation']);
  await context.setGeolocation({
    latitude: 47.6062,
    longitude: -122.3321
  });
  
  // Navigate to the page and wait for network to be idle
  await page.goto(BASE_URL, { waitUntil: 'networkidle' });
  
  // Wait for Blazor to fully initialize and render
  await page.waitForLoadState('domcontentloaded');
  await page.waitForLoadState('networkidle');
  
  // Give Blazor extra time to initialize the router
  await page.waitForTimeout(2000);
});

test.describe('Restaurant List Tests', () => {
  
  test('HomePage: On load, displays nearby restaurants after enabling location', async ({ page }) => {
    // Wait for the location prompt to appear
    const enableButton = page.locator('button', { hasText: 'Enable Location' });
    await expect(enableButton).toBeVisible({ timeout: 10000 });
    
    // Click the button to request location and load restaurants
    await enableButton.click();
    
    // Wait for restaurants to load automatically
    const restaurantCards = page.locator('.restaurant-card');
    await expect(restaurantCards.first()).toBeVisible({ timeout: 15000 });

    // Assert - Verify at least one restaurant is displayed
    const count = await restaurantCards.count();
    expect(count).toBeGreaterThan(0);
  });

  test('HomePage: Shows restaurant details', async ({ page }) => {
    // Wait for the location prompt to appear
    const enableButton = page.locator('button', { hasText: 'Enable Location' });
    await expect(enableButton).toBeVisible({ timeout: 10000 });
    
    // Click the button to request location and load restaurants
    await enableButton.click();
    
    // Wait for restaurants to load
    const restaurantCards = page.locator('.restaurant-card');
    await expect(restaurantCards.first()).toBeVisible({ timeout: 15000 });

    // Assert - Verify at least one restaurant has required elements
    const firstCard = restaurantCards.first();
    await expect(firstCard.locator('h3, h4, .restaurant-name')).toBeVisible();
    await expect(firstCard.locator('.address, address')).toBeVisible();
    await expect(firstCard.locator('.rating, .stars').first()).toBeVisible();
  });

  test('RestaurantCard: When clicked, navigates to details page', async ({ page }) => {
    // Wait for the location prompt to appear
    const enableButton = page.locator('button', { hasText: 'Enable Location' });
    await expect(enableButton).toBeVisible({ timeout: 10000 });
    
    // Click the button to request location and load restaurants
    await enableButton.click();
    
    // Wait for restaurants to load
    const restaurantCards = page.locator('.restaurant-card');
    await expect(restaurantCards.first()).toBeVisible({ timeout: 15000 });

    // Act - Click the first restaurant card
    await restaurantCards.first().click();

    // Assert - Wait for navigation or content change
    await page.waitForTimeout(1000);
    
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

    // Wait for the location prompt to appear
    const enableButton = page.locator('button', { hasText: 'Enable Location' });
    await expect(enableButton).toBeVisible({ timeout: 10000 });
    
    // Click button to trigger geolocation and API call
    await enableButton.click();
    
    // Wait for restaurants to appear (confirming API was called)
    const restaurantCards = page.locator('.restaurant-card');
    await expect(restaurantCards.first()).toBeVisible({ timeout: 15000 });

    // Assert - Verify the API was called with coordinates
    expect(apiCallMade).toBeTruthy();
  });

  test('GeolocationService: Verifies JavaScript interop works', async ({ page }) => {
    // Arrange
    await page.goto(BASE_URL);
    await page.waitForLoadState('networkidle');

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
    const enableButton = page.locator('button', { hasText: 'Enable Location' });
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
    const enableButton = page.locator('button', { hasText: 'Enable Location' });
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
