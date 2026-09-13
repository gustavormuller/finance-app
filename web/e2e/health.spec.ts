import { expect, test } from '@playwright/test';

// The browser asks the Vite dev server on :5173 for /api/health and Vite forwards it
// to the API on :5080. That hop is the reason this test exists: without the proxy the
// request never reaches the API and the route renders its degraded state.
test('the health route renders the status reported by the API through the proxy', async ({
  page,
}) => {
  await page.goto('/');

  await expect(page.getByTestId('health-status')).toHaveText('ok');
  await expect(page.getByTestId('health-database')).toHaveText('ok');
});
