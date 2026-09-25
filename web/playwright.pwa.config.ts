import { defineConfig, devices } from '@playwright/test';

/**
 * Spec 022: the service worker against a production build, which the dev-server suite in
 * playwright.config.ts never registers. The spec builds the app and runs `vite preview`
 * itself (e2e-pwa/preview.ts), because it stops the server and rebuilds mid-run.
 *
 *   API_URL=http://localhost:5080 PREVIEW_PORT=4173 npm run e2e:pwa
 *
 * The API at API_URL runs in Development (for the dev sign-in) with App__Origin set to
 * http://localhost:$PREVIEW_PORT. The installability test needs a full browser, installed
 * Google Chrome by default; PWA_INSTALL_CHANNEL=msedge or =chromium picks another.
 */
const BASE_URL = `http://localhost:${process.env.PREVIEW_PORT ?? 4173}`;

export default defineConfig({
  testDir: './e2e-pwa',
  workers: 1,
  retries: 0,
  timeout: 120_000,
  reporter: 'list',
  use: {
    ...devices['Desktop Chrome'],
    baseURL: BASE_URL,
  },
});
