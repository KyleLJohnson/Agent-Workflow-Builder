import { type Page, type TestInfo } from "@playwright/test";
import * as fs from "fs";
import * as path from "path";

const COVERAGE_DIR = path.resolve(__dirname, "../.nyc_output");

/**
 * Collects Istanbul coverage data from the browser's `window.__coverage__` object.
 * Call this in `test.afterEach` to capture coverage for each test.
 *
 * Prerequisites:
 * - Start the Vite dev server with VITE_COVERAGE=true
 * - The vite-plugin-istanbul will instrument source code and expose `window.__coverage__`
 */
export async function collectCoverage(page: Page, testInfo: TestInfo) {
  const coverage = await page.evaluate(() => {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    return (window as any).__coverage__ ?? null;
  });

  if (!coverage) {
    return;
  }

  if (!fs.existsSync(COVERAGE_DIR)) {
    fs.mkdirSync(COVERAGE_DIR, { recursive: true });
  }

  const safeName = testInfo.title
    .replace(/[^a-z0-9]/gi, "_")
    .substring(0, 80);
  const fileName = `coverage-${safeName}-${testInfo.testId}.json`;

  fs.writeFileSync(
    path.join(COVERAGE_DIR, fileName),
    JSON.stringify(coverage),
    "utf-8"
  );
}
