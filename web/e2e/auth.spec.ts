import { expect, test } from '@playwright/test';

import { devLogin, uniqueEmail } from './support';

test('an unauthenticated visit to / lands on the login page', async ({ page }) => {
  await page.goto('/');

  await expect(page).toHaveURL(/\/login$/);
  await expect(page.getByRole('link', { name: 'Entrar com o Google' })).toBeVisible();
});

test('a signed-in visitor sees their display name on /', async ({ page }) => {
  await devLogin(page, uniqueEmail('signed-in'), 'Ada Lovelace');

  await page.goto('/');

  await expect(page.getByTestId('current-user')).toHaveText('Ada Lovelace');
  await expect(page.getByRole('link', { name: 'Entrar com o Google' })).toBeHidden();
});

test('logging out returns to the login page and leaves / protected', async ({ page }) => {
  await devLogin(page, uniqueEmail('logout'), 'Grace Hopper');

  await page.goto('/');
  await expect(page.getByTestId('current-user')).toHaveText('Grace Hopper');

  await page.getByRole('button', { name: 'Sair' }).click();

  await expect(page).toHaveURL(/\/login$/);

  // The cookie is gone, not just the client-side state.
  await page.goto('/');
  await expect(page).toHaveURL(/\/login$/);
});
