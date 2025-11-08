# Running La'Caj Seafood E2E Test

Quick guide to run the comprehensive E2E test for La'Caj Seafood comic generation.

## Prerequisites Checklist

- [ ] Node.js 18+ installed
- [ ] Playwright installed: `npm install` (in tests/e2e directory)
- [ ] Playwright browsers installed: `npx playwright install chromium`
- [ ] Google Maps API key configured in user secrets
- [ ] Azure OpenAI API key configured in user secrets
- [ ] Azurite running (for caching)

## Step-by-Step Instructions

### 1. Start Azurite (Terminal 1)

```powershell
# From project root
azurite
```

Keep this terminal running.

### 2. Start the API (Terminal 2)

```powershell
# From project root
cd src/Po.SeeReview.Api
dotnet run --urls "http://localhost:5000"
```

Wait for: `Now listening on: http://localhost:5000`

Keep this terminal running.

### 3. Run the E2E Test (Terminal 3)

```powershell
# From project root
cd tests/e2e

# Run La'Caj tests only (headless)
npm run test:lacaj

# OR run with visible browser to watch the test
npm run test:lacaj:headed

# OR run with debug mode to step through
npm run test:lacaj:debug
```

## What the Test Does

The test simulates a real user flow:

1. **Enable Location** (38.8280156, -76.9088866 - near La'Caj)
2. **Wait for Restaurants** to load on homepage
3. **Find La'Caj Seafood** in the restaurant list
4. **Click the Card** to navigate to comic page
5. **Verify Reviews** are fetched (230+ reviews from Google Maps)
6. **Generate Comic** (or verify auto-generated comic)
7. **Verify Comic Image** is displayed
8. **Verify Restaurant Details** (name, address, rating)

## Expected Results

### Test 1: Full Flow (90-120 seconds)
✅ Location enabled
✅ Found 1+ restaurants
✅ Found La'Caj Seafood
✅ Navigated to comic page
✅ Reviews loaded (5+ reviews)
✅ Comic generated and displayed
✅ Restaurant details visible

### Test 2: API Integration (5-10 seconds)
✅ API calls tracked
✅ Google Maps API returned reviews
✅ La'Caj Place ID in API response

### Test 3: Cache Verification (10-15 seconds)
✅ First visit fetched from API
✅ Second visit used cache or re-validated
✅ Total API calls reasonable

## Troubleshooting

### Test fails: "Enable Location button not found"
- Check API is running on port 5000
- Check Blazor app is accessible at http://localhost:5000
- Wait longer for Blazor to initialize

### Test fails: "La'Caj Seafood not found"
- Verify geolocation is set correctly in test
- Check Google Maps API is returning nearby restaurants
- API key might be missing or invalid

### Test fails: "No reviews found"
- Check Google Maps API key is configured
- Check API key has Places API (New) enabled
- Look for error messages in API logs

### Test fails: "Comic not generated"
- Check Azure OpenAI API key is configured
- This can take 60-90 seconds - be patient
- Check API logs for OpenAI errors
- Verify OpenAI endpoint and deployment name

### Test timeout
- Increase timeout in playwright.config.ts
- Check network connection
- Check API performance

## Viewing Test Reports

After test run:

```powershell
npm run report
```

This opens an HTML report showing:
- Test results
- Screenshots (on failure)
- Traces (for debugging)
- Console logs
- Network activity

## Test Output Example

```
🎬 Starting full E2E test for La'Caj Seafood comic generation...
📍 Step 1: Enabling location...
✅ Location enabled
🔄 Step 2: Waiting for restaurants to load...
✅ Found 3 restaurants
🔍 Step 3: Looking for La'Caj Seafood...
✅ Found restaurant: La'Caj Seafood
📊 Reviews: 230 reviews
🖱️ Step 4: Clicking La'Caj Seafood card...
✅ Navigated to: http://localhost:5000/comic/ChIJB0Oz_rC9t4kRrRgfCQ27RKQ
📝 Step 5: Verifying reviews are loaded...
✅ Found 5 review elements on page
📝 Sample review text: I visited from out of town, looking for a good brunch spot...
🎨 Step 6: Checking for comic generation UI...
✅ Found "Generate Comic" button
🎨 Clicked generate comic button
⏳ Waiting for comic to generate (may take 30-60 seconds)...
✅ Comic generated and displayed!
🖼️ Comic image src: data:image/png;base64,iVBORw0KGgoAAAANSUhEUg...
📋 Step 7: Verifying restaurant details...
✅ Restaurant name displayed: La'Caj Seafood
📍 Address displayed: 4531 Telfair Blvd #110, Camp Springs, MD 20746
⭐ Rating displayed: 3.9 ★
🎉 E2E test completed successfully!
```

## CI/CD Integration

To run in CI/CD pipeline:

```yaml
- name: Run La'Caj E2E Tests
  run: |
    cd tests/e2e
    npm ci
    npx playwright install --with-deps chromium
    npm run test:lacaj
```

## Additional Resources

- [Playwright Documentation](https://playwright.dev)
- [Google Places API (New)](https://developers.google.com/maps/documentation/places/web-service/place-details)
- [Azure OpenAI Documentation](https://learn.microsoft.com/en-us/azure/ai-services/openai/)
