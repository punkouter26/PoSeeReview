import { defineConfig, devices } from '@playwright/test';

const smokeOnly = process.env.E2E_SMOKE === '1';

export default defineConfig({
  testDir: './tests',
  fullyParallel: !smokeOnly,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  workers: smokeOnly ? 1 : (process.env.CI ? 1 : undefined),
  reporter: 'html',
  grep: smokeOnly ? /@smoke/ : undefined,
  
  use: {
    baseURL: 'https://localhost:5001',
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
    ignoreHTTPSErrors: true,
  },

  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
    {
      name: 'mobile-chrome',
      use: { ...devices['Pixel 5'] },
    },
  ],

  // Build the client before starting, then reuse the existing running API
  webServer: [
    {
      command: 'pwsh -NoProfile -Command "Set-Location C:\\; dotnet build c:/Users/punko/Downloads/PoSeeReview/src/PoSeeReview.Client/PoSeeReview.Client.csproj --configuration Debug --no-restore -v q"',
      reuseExistingServer: true,
    },
    {
      command: 'pwsh -NoProfile -Command "Set-Location C:\\; dotnet run --project c:/Users/punko/Downloads/PoSeeReview/src/PoSeeReview.Api/PoSeeReview.Api.csproj --launch-profile https"',
      url: 'https://localhost:5001/api/health/live',
      reuseExistingServer: true,
      ignoreHTTPSErrors: true,
      timeout: 120 * 1000,
      stdout: 'pipe',
      stderr: 'pipe',
    },
  ],
});
