# E2E Tests (TypeScript + Playwright)

This directory contains end-to-end tests for PoSeeReview using Playwright.

## Prerequisites

- Node.js 18+
- `npm install`
- `npx playwright install chromium`
- .NET 10 SDK (for automatic API startup)

## Run Tests

```powershell
cd tests\e2e
npm test
```

Playwright starts the API on `https://localhost:5001`, waits for `/api/health/live`, runs tests, and shuts down when complete.

## Commands

- `npm test` - Run full suite
- `npm run test:smoke` - Run smoke-tagged tests only
- `npm run test:ui` - Interactive Playwright UI mode
- `npm run test:debug` - Debug with Playwright Inspector
- `npm run test:headed` - Visible browser mode
- `npm run test:lacaj` - Run only La'Caj flow tests
- `npm run test:lacaj:headed` - La'Caj tests in headed mode
- `npm run test:lacaj:debug` - La'Caj tests in debug mode
- `npm run report` - Open HTML report

## Current Test Files

- `tests/dev-session.spec.ts`
- `tests/geolocation.spec.ts`
- `tests/lacaj-comic-generation.spec.ts`
- `tests/debug.spec.ts`

## Troubleshooting

### Connection refused

Ensure no stale API process is holding ports and rerun `npm test`.

### Browser not installed

Run:

```powershell
npx playwright install chromium
```
