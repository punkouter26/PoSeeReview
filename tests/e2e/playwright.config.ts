import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
  testDir: './tests',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  workers: process.env.CI ? 1 : undefined,
  reporter: 'html',
  
  use: {
    baseURL: 'http://localhost:5000',
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
  },

  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],

  // Automatically start the API before running tests
  webServer: {
    command: 'dotnet run --project ../../src/Po.SeeReview.Api/Po.SeeReview.Api.csproj',
    url: 'http://localhost:5000/api/health/live',
    reuseExistingServer: !process.env.CI,
    timeout: 120 * 1000, // 2 minutes for build + startup
    stdout: 'pipe',
    stderr: 'pipe',
    env: {
      ASPNETCORE_ENVIRONMENT: 'Test',
      ASPNETCORE_URLS: 'http://localhost:5000',
    },
  },
});
