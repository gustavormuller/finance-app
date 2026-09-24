import { defineConfig, devices } from '@playwright/test';

const BASE_URL = process.env.BASE_URL ?? 'http://localhost:5173';

/** The specs that trigger a manual market-data sync; see `projects`. */
const SYNCING_SPECS = [/market-data\.spec\.ts/, /investments\.spec\.ts/, /returns\.spec\.ts/];

export default defineConfig({
  testDir: './e2e',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  reporter: process.env.CI ? 'list' : [['list'], ['html', { open: 'never' }]],
  use: {
    baseURL: BASE_URL,
    trace: 'on-first-retry',
  },
  // A manual sync is refused (429) while another is running, and specs 006, 007 and 008
  // all trigger one. Test 26 asserts the 202, so the syncing specs run one project after
  // another: market-data, then investments, then returns. Every other spec runs alongside.
  projects: [
    {
      name: 'chromium',
      testIgnore: SYNCING_SPECS,
      use: { ...devices['Desktop Chrome'] },
    },
    {
      name: 'market-data',
      testMatch: /market-data\.spec\.ts/,
      use: { ...devices['Desktop Chrome'] },
    },
    {
      name: 'investments',
      testMatch: /investments\.spec\.ts/,
      dependencies: ['market-data'],
      use: { ...devices['Desktop Chrome'] },
    },
    {
      name: 'returns',
      testMatch: /returns\.spec\.ts/,
      dependencies: ['investments'],
      use: { ...devices['Desktop Chrome'] },
    },
  ],
  // scripts/verify-e2e.sh starts Postgres and the API; Playwright starts the
  // frontend itself and reuses an already-running dev server when there is one.
  webServer: {
    command: 'npm run dev',
    url: BASE_URL,
    reuseExistingServer: !process.env.CI,
    timeout: 120_000,
  },
});
