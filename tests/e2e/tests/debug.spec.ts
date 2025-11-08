import { test, expect } from '@playwright/test';
import * as fs from 'fs';
import * as path from 'path';

/**
 * Debug test to capture browser state and help diagnose issues
 * Creates: debug-screenshot.png, debug-content.html, debug-console.txt
 */

const BASE_URL = 'http://localhost:5000';

test('Debug: Screenshot and HTML dump', async ({ page }) => {
  const consoleMessages: string[] = [];
  const pageErrors: string[] = [];

  // Capture console messages
  page.on('console', msg => {
    consoleMessages.push(`[${msg.type()}] ${msg.text()}`);
  });

  // Capture page errors
  page.on('pageerror', err => {
    pageErrors.push(`Error: ${err.message}\nStack: ${err.stack}`);
  });

  try {
    // Navigate to the page
    await page.goto(BASE_URL, { waitUntil: 'networkidle' });
    
    // Wait for Blazor to initialize
    await page.waitForLoadState('domcontentloaded');
    await page.waitForLoadState('networkidle');
    await page.waitForTimeout(5000);

    // Get page content
    const htmlContent = await page.content();
    
    // Take screenshot
    await page.screenshot({ 
      path: 'debug-screenshot.png',
      fullPage: true 
    });

    // Write HTML content to file
    fs.writeFileSync('debug-content.html', htmlContent, 'utf-8');

    // Write console log to file
    const consoleLog = [
      '=== CONSOLE MESSAGES ===',
      ...consoleMessages,
      '',
      '=== PAGE ERRORS ===',
      ...pageErrors
    ].join('\n');
    
    fs.writeFileSync('debug-console.txt', consoleLog, 'utf-8');

    console.log('Debug files created:');
    console.log('  - debug-screenshot.png');
    console.log('  - debug-content.html');
    console.log('  - debug-console.txt');
    console.log(`Console messages: ${consoleMessages.length}`);
    console.log(`Page errors: ${pageErrors.length}`);

  } catch (error) {
    console.error('Debug test failed:', error);
    
    // Still try to capture what we can
    try {
      await page.screenshot({ path: 'debug-screenshot-error.png', fullPage: true });
      const content = await page.content();
      fs.writeFileSync('debug-content-error.html', content, 'utf-8');
    } catch (innerError) {
      console.error('Failed to capture error state:', innerError);
    }
    
    throw error;
  }
});
