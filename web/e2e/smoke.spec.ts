import { expect, test } from '@playwright/test';

test('application shell renders', async ({ page }) => {
  await page.goto('/');

  await expect(page.getByRole('heading', { name: 'Personal Finance' })).toBeVisible();
});

test('api health endpoint is reachable through the dev proxy', async ({ request }) => {
  const response = await request.get('/health');

  expect(response.ok()).toBeTruthy();
  expect(await response.json()).toEqual({ status: 'healthy' });
});
