import { expect, test } from '@playwright/test';

/**
 * Spec 012 E2E test 7: the theme choice survives a reload, and is applied before the
 * page renders (the script in index.html), not only once React has mounted.
 */
test('the theme chosen is kept across a reload', async ({ page }) => {
  await page.goto('/login');
  const html = page.locator('html');

  await page.getByRole('button', { name: 'Escuro' }).click();
  await expect(html).toHaveClass(/\bdark\b/);

  await page.reload();
  await expect(html).toHaveClass(/\bdark\b/);
  await expect(page.getByRole('button', { name: 'Escuro' })).toHaveAttribute('aria-pressed', 'true');

  await page.getByRole('button', { name: 'Claro' }).click();
  await expect(html).not.toHaveClass(/\bdark\b/);

  await page.reload();
  await expect(html).not.toHaveClass(/\bdark\b/);
});
