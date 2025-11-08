# E2E Tests (TypeScript + Playwright)

This directory contains end-to-end tests for the PoSeeReview application using TypeScript and Playwright.

## Prerequisites

- Node.js 18+ installed
- Dependencies installed: `npm install`
- Playwright browsers installed: `npx playwright install chromium`
- **.NET 9.0 SDK installed** (for automatic API startup)

## Running Tests

### Quick Start (Automatic API Startup)

The tests will **automatically start and stop the API** when you run them:

```powershell
cd tests\e2e
npm test
```

Playwright will:
1. Build and start the API on `http://localhost:5000`
2. Wait for the `/api/health/live` endpoint to respond
3. Run all tests
4. Shut down the API automatically

**Note**: First run will take ~2 minutes due to building the .NET project.

### Manual API Control (Optional)

If you want to start the API manually (e.g., for debugging):

In one terminal:

```powershell
cd src\Po.SeeReview.Api
dotnet run --urls http://localhost:5000
```

Then run tests in another terminal:

```powershell
cd tests\e2e
npm test
```

The tests will detect the running server and reuse it (see `reuseExistingServer` in `playwright.config.ts`).

## Available Commands

- `npm test` - Run all tests in headless mode
- `npm run test:ui` - Run tests with Playwright UI mode (interactive)
- `npm run test:debug` - Run tests in debug mode with Playwright Inspector
- `npm run test:headed` - Run tests with visible browser
- `npm run test:lacaj` - Run only La'Caj Seafood comic generation tests
- `npm run test:lacaj:headed` - Run La'Caj tests with visible browser
- `npm run test:lacaj:debug` - Debug La'Caj tests with Playwright Inspector
- `npm run report` - Show HTML test report

## Test Structure

- `tests/geolocation.spec.ts` - 7 tests for restaurant list and geolocation functionality
  - Homepage displays restaurants after enabling location
  - Restaurant cards show details (name, address, rating)
  - Clicking a restaurant navigates to details page
  - API receives correct coordinates
  - JavaScript interop works
  - Distance from user location is displayed

- `tests/lacaj-comic-generation.spec.ts` - 3 comprehensive E2E tests for La'Caj Seafood
  - **Full Flow Test**: Enable location → Select La'Caj → Verify reviews → Generate comic (90+ seconds)
  - **API Integration Test**: Verify Google Maps API returns reviews for La'Caj
  - **Cache Verification Test**: Verify second visit uses cached reviews

### La'Caj Seafood E2E Test Details

The `lacaj-comic-generation.spec.ts` test provides comprehensive coverage of the entire comic generation flow:

**Test Flow:**
1. 📍 Enable geolocation (simulated near La'Caj in Camp Springs, MD)
2. 🔄 Wait for nearby restaurants to load
3. 🔍 Find and click La'Caj Seafood restaurant card
4. 📝 Verify reviews are fetched from Google Maps API (230+ reviews)
5. 🎨 Trigger comic generation (or verify auto-generation)
6. ✅ Verify comic is displayed with image
7. 📋 Verify restaurant details (name, address, rating)

**Prerequisites for La'Caj Test:**
- Google Maps API key configured in user secrets
- Azure OpenAI API key configured (for comic generation)
- Azurite running for caching
- La'Caj Seafood Place ID: `ChIJB0Oz_rC9t4kRrRgfCQ27RKQ`

**Running Just La'Caj Tests:**
```powershell
npx playwright test lacaj-comic-generation.spec.ts
```

**Expected Duration:**
- Full flow test: ~90-120 seconds (includes AI comic generation)
- API integration test: ~5-10 seconds
- Cache verification test: ~10-15 seconds
  - Review count is displayed

- `tests/debug.spec.ts` - 1 debug test for troubleshooting
  - Captures console messages
  - Takes full-page screenshot
  - Dumps HTML content
  - Creates: `debug-screenshot.png`, `debug-content.html`, `debug-console.txt`

## Configuration

- Base URL: `http://localhost:5000`
- Browser: Chromium only (Desktop Chrome)
- Geolocation: Mocked to Seattle (47.6062, -122.3321)
- Timeouts: 10s for buttons, 15s for restaurant cards

## Troubleshooting

### Tests fail with ERR_CONNECTION_REFUSED

Make sure the API is running on port 5000. Check with:

```powershell
Get-NetTCPConnection -LocalPort 5000
```

### Tests fail with 404 "Not found"

This is a known routing issue with Index.razor. Try:
1. Run tests in headed mode: `npm run test:headed`
2. Check if manual browser access works
3. Run debug test to capture page state: `npx playwright test debug.spec.ts`

### Browser not installed

Run: `npx playwright install chromium`

## Migrated from C#

These tests were converted from C# Playwright/NUnit tests in `tests/Po.SeeReview.E2ETests/`. 
The TypeScript version provides:
- Better IDE support and autocomplete
- More standard Playwright patterns
- Easier to maintain and extend
- Same test coverage as C# version
