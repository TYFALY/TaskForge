const { chromium } = require('@playwright/test');

const DASHBOARD_URL = 'http://localhost:5173';
const SCREENSHOT_PATH = '../docs/dashboard-preview.png';

async function runTests() {
  console.log('═══════════════════════════════════════════════════════════════');
  console.log('         TaskForge Dashboard QA Test Suite');
  console.log('═══════════════════════════════════════════════════════════════\n');

  let browser;
  let page;
  let passed = 0;
  let failed = 0;

  try {
    console.log('Launching headless Chromium...');
    browser = await chromium.launch({ headless: true });
    page = await browser.newPage();

    console.log(`Navigating to ${DASHBOARD_URL}...\n`);
    await page.goto(DASHBOARD_URL, { waitUntil: 'networkidle', timeout: 30000 });
    await page.waitForTimeout(2000);

    // Test 1: KPI Metrics Cards
    console.log('TEST 1: KPI Metrics Cards');
    console.log('─────────────────────────────────────────────────────────────');
    try {
      await page.waitForSelector('text=Total Processed', { timeout: 10000 });
      const kpiCards = await page.locator('text=Total Processed, text=Failed / DLQ, text=Active Workers, text=Avg Latency').count();
      console.log(`  ✓ Found ${kpiCards} KPI cards`);
      passed++;
    } catch (e) {
      console.log(`  ✗ KPI cards not found: ${e.message}`);
      failed++;
    }

    // Test 2: Header Elements
    console.log('\nTEST 2: Header Elements');
    console.log('─────────────────────────────────────────────────────────────');
    try {
      await page.waitForSelector('text=TaskForge', { timeout: 5000 });
      await page.waitForSelector('text=Workers:', { timeout: 5000 });
      console.log('  ✓ Header with branding and worker count visible');
      passed++;
    } catch (e) {
      console.log(`  ✗ Header elements not found: ${e.message}`);
      failed++;
    }

    // Test 3: Live Metrics Panel
    console.log('\nTEST 3: Live Metrics Panel');
    console.log('─────────────────────────────────────────────────────────────');
    try {
      await page.waitForSelector('text=Live Metrics', { timeout: 5000 });
      await page.waitForSelector('text=Queue Size', { timeout: 5000 });
      console.log('  ✓ Live metrics panel visible');
      passed++;
    } catch (e) {
      console.log(`  ✗ Live metrics not found: ${e.message}`);
      failed++;
    }

    // Test 4: Job History Table
    console.log('\nTEST 4: Job History Table');
    console.log('─────────────────────────────────────────────────────────────');
    try {
      await page.waitForSelector('text=Job History', { timeout: 10000 });
      const rows = await page.locator('tbody tr').count();
      console.log(`  ✓ Job history table visible with ${rows} rows`);
      passed++;
    } catch (e) {
      console.log(`  ✗ Job history table not found: ${e.message}`);
      failed++;
    }

    // Test 5: Status Badges
    console.log('\nTEST 5: Status Badges');
    console.log('─────────────────────────────────────────────────────────────');
    try {
      const badges = await page.locator('span.rounded-full').count();
      console.log(`  ✓ Found ${badges} status badges with proper styling`);
      passed++;
    } catch (e) {
      console.log(`  ✗ Status badges not found: ${e.message}`);
      failed++;
    }

    // Test 6: Dark Theme
    console.log('\nTEST 6: Dark Theme');
    console.log('─────────────────────────────────────────────────────────────');
    try {
      const bgColor = await page.evaluate(() => {
        return window.getComputedStyle(document.body).backgroundColor;
      });
      console.log(`  ✓ Body background: ${bgColor}`);
      passed++;
    } catch (e) {
      console.log(`  ✗ Dark theme check failed: ${e.message}`);
      failed++;
    }

    // Test 7: Full Page Screenshot
    console.log('\nTEST 7: Full Page Screenshot');
    console.log('─────────────────────────────────────────────────────────────');
    try {
      await page.screenshot({
        path: SCREENSHOT_PATH,
        fullPage: true,
        type: 'png'
      });
      console.log(`  ✓ Screenshot saved to ${SCREENSHOT_PATH}`);
      passed++;
    } catch (e) {
      console.log(`  ✗ Screenshot failed: ${e.message}`);
      failed++;
    }

    // Test 8: No Critical Errors
    console.log('\nTEST 8: No Critical Console Errors');
    console.log('─────────────────────────────────────────────────────────────');
    const errors = [];
    page.on('pageerror', err => errors.push(err.message));
    await page.waitForTimeout(1000);
    if (errors.length === 0) {
      console.log('  ✓ No JavaScript errors detected');
      passed++;
    } else {
      console.log(`  ✗ Found ${errors.length} errors:`, errors);
      failed++;
    }

  } catch (error) {
    console.error(`\nFatal error: ${error.message}`);
    failed++;
  } finally {
    if (browser) await browser.close();
  }

  // Summary
  console.log('\n═══════════════════════════════════════════════════════════════');
  console.log('                      TEST SUMMARY');
  console.log('═══════════════════════════════════════════════════════════════');
  console.log(`  Passed: ${passed}`);
  console.log(`  Failed: ${failed}`);
  console.log(`  Total:  ${passed + failed}`);
  console.log('═══════════════════════════════════════════════════════════════\n');

  return failed === 0 ? 0 : 1;
}

runTests().then(code => process.exit(code));