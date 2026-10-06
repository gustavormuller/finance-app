import { expect, test } from '@playwright/test';

import { devLogin, uniqueEmail } from './support';

/** Every page behind the sign-in, the ones outside the navigation included. */
const PROTECTED = ['/', '/transactions', '/accounts', '/import', '/categories', '/investments', '/investments/returns', '/market-data', '/settings'];

test('an unauthenticated visit to any page lands on the login page', async ({ page }) => {
  for (const path of PROTECTED) {
    await page.goto(path);

    await expect(page, path).toHaveURL(/\/login$/);
    await expect(page.getByRole('link', { name: 'Entrar com o Google' })).toBeVisible();
  }
});

/**
 * The Google round trip itself is not E2E-reachable (Playwright cannot complete Google's
 * consent screen), but the addresses its callback sends the browser back to are.
 */
test('the login page explains each way a sign-in can come back, and nothing else', async ({ page }) => {
  const messages: Record<string, string> = {
    unverified:
      'O Google informa que este e-mail não está verificado. Verifique-o com o Google e tente de novo — nenhuma conta foi criada.',
    cancelled: 'A entrada foi cancelada. Nada foi criado, e você pode tentar de novo quando quiser.',
    auth_failed: 'Não foi possível concluir a entrada. Tente de novo; se continuar acontecendo, o problema é do nosso lado.',
  };

  for (const [code, message] of Object.entries(messages)) {
    await page.goto(`/login?error=${code}`);
    await expect(page.getByRole('alert')).toHaveText(message);
  }

  // A stale or made-up link renders no empty box.
  await page.goto('/login?error=whatever&notice=whatever');
  await expect(page.getByRole('heading', { name: 'Entrar' })).toBeVisible();
  await expect(page.getByRole('alert')).toHaveCount(0);
  await expect(page.getByRole('status')).toHaveCount(0);
  await expect(page.getByRole('link', { name: 'Entrar com o Google' })).toHaveAttribute('href', '/api/auth/google');
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
