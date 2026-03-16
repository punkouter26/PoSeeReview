import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
  testDir: './tests',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  workers: process.env.CI ? 1 : undefined,
  reporter: 'html',
  
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
      command: 'dotnet build ../../src/Po.SeeReview.Client/Po.SeeReview.Client.csproj --configuration Debug --no-restore -v q',
      reuseExistingServer: true,
    },
    {
      command: 'dotnet run --project ../../src/Po.SeeReview.Api/Po.SeeReview.Api.csproj --launch-profile https',
      url: 'https://localhost:5001/api/health/live',
      reuseExistingServer: true,
      ignoreHTTPSErrors: true,
      timeout: 120 * 1000,
      stdout: 'pipe',
      stderr: 'pipe',
    },
  ],
});
