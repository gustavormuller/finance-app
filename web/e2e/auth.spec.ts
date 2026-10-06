import { expect, test, type Page } from '@playwright/test';

import { createAccount, devLogin, uniqueEmail } from './support';

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

  // 024: an account Google gave no name is shown by its e-mail.
  const nameless = uniqueEmail('signed-in-nameless');
  await devLogin(page, nameless, '');
  await page.goto('/');
  await expect(page.getByTestId('current-user')).toHaveText(nameless);
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

/** Signs the browser out from a second tab, so the page under test still believes it is signed in. */
async function signOutElsewhere(page: Page) {
  const other = await page.context().newPage();
  await other.goto('/');
  await other.getByRole('button', { name: 'Sair' }).click();
  await expect(other).toHaveURL(/\/login$/);
  await other.close();
}

/** 024: a write from a tab whose session ended in another one. */
test('a write after the session ended in another tab is explained in Portuguese', async ({ page }) => {
  test.fixme(true, 'Bug: a 401 has no problem body, so the page shows the client\'s English fallback "Request failed (401)".');

  await devLogin(page, uniqueEmail('e2e-ended-write'), 'Ada Lovelace');
  await createAccount(page, 'Nubank');
  await page.goto('/transactions');
  await signOutElsewhere(page);

  await page.getByRole('button', { name: 'Novo lançamento' }).click();
  await page.getByLabel('Conta', { exact: true }).selectOption({ label: 'Nubank' });
  await page.getByLabel('Categoria', { exact: true }).selectOption({ label: 'Lazer' });
  await page.getByLabel('Valor').fill('10');
  await page.getByLabel('Data').fill('2026-09-10');
  await page.getByLabel('Descrição').fill('Depois de sair');
  await page.getByRole('button', { name: 'Criar lançamento' }).click();

  await expect(page.getByRole('alert')).toContainText('sessão');
  await expect(page.getByRole('alert')).not.toContainText('Request failed');
});

/** 024: Sair from a tab whose session already ended. */
test('Sair from a tab whose session already ended lands on the login page', async ({ page }) => {
  test.fixme(true, 'Bug: the logout answers 401 and the sidebar shows "Não foi possível sair." instead of leaving.');

  await devLogin(page, uniqueEmail('e2e-ended-logout'), 'Grace Hopper');
  await page.goto('/');
  await signOutElsewhere(page);

  await page.getByRole('button', { name: 'Sair' }).click();

  await expect(page).toHaveURL(/\/login$/);
});
