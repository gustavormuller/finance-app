import { expect, test } from '@playwright/test';

import { createAccount, devLogin, uniqueEmail } from './support';

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

/**
 * 012 amendment 1, test 9: a border colour next to `glass` shows. A new account is
 * selected, and the selected card is outlined in the primary colour, not `--border`.
 */
test('the selected account card has the primary outline', async ({ page }) => {
  await devLogin(page, uniqueEmail('glass'), 'Glass');
  await createAccount(page, 'Conta contornada');

  const primary = await page.evaluate(() => {
    const probe = document.createElement('span');
    probe.style.color = 'var(--primary)';
    document.body.append(probe);
    const color = getComputedStyle(probe).color;
    probe.remove();
    return color;
  });
  const card = page.getByRole('listitem').filter({ has: page.getByRole('link', { name: 'Conta contornada', exact: true }) });

  // Retried: the card's colours transition in.
  await expect(card).toHaveCSS('border-top-color', primary);
});
