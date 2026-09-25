import { expect, test } from '@playwright/test';

/**
 * Spec 012 E2E test 7: the theme choice survives a reload, and is applied before the
 * page renders (the script in index.html), not only once React has mounted.
 */
test('the theme chosen is kept across a reload', async ({ page }) => {
  await page.goto('/login');
  const html = page.locator('html');
  // Spec 022 test 7: the browser's toolbar follows the theme applied, whatever the system says.
  const themeColors = () =>
    page.locator('meta[name="theme-color"]').evaluateAll((metas) => metas.map((meta) => meta.getAttribute('content')));

  await page.getByRole('button', { name: 'Escuro' }).click();
  await expect(html).toHaveClass(/\bdark\b/);

  await page.reload();
  await expect(html).toHaveClass(/\bdark\b/);
  await expect(page.getByRole('button', { name: 'Escuro' })).toHaveAttribute('aria-pressed', 'true');
  expect(await themeColors()).toEqual(['#0b0c12', '#0b0c12']);

  await page.getByRole('button', { name: 'Claro' }).click();
  await expect(html).not.toHaveClass(/\bdark\b/);

  await page.reload();
  await expect(html).not.toHaveClass(/\bdark\b/);
  expect(await themeColors()).toEqual(['#f1f2f7', '#f1f2f7']);
});
