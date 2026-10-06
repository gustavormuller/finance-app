import { expect, request, test } from '@playwright/test';

import { createAccount, devLogin, uniqueEmail } from './support';

const SESSION_COOKIE = '.AspNetCore.Identity.Application';

/** Spec 023 test 12; 024: the button waits for the exact address (023 decision 6). */
test('a user deletes their account, lands on the login page, and the old session reaches nothing', async ({
  page,
  baseURL,
}) => {
  const email = uniqueEmail('delete-account');
  await devLogin(page, email, 'Conta Excluída');
  const session = (await page.context().cookies()).find((cookie) => cookie.name === SESSION_COOKIE);
  expect(session).toBeDefined();

  await page.goto('/settings');
  const zone = page.getByRole('region', { name: 'Excluir minha conta' });
  const field = zone.getByLabel('Digite seu e-mail para confirmar');
  const button = zone.getByRole('button', { name: 'Excluir definitivamente' });

  // The last section of the page, saying what goes with the account.
  await expect(page.getByRole('region').last()).toHaveAccessibleName('Excluir minha conta');
  for (const phrase of ['contas', 'lançamentos', 'importações', 'investimentos', 'análises de IA', 'Não dá para desfazer.']) {
    await expect(zone).toContainText(phrase);
  }
  await expect(zone).toContainText(`Para confirmar, digite ${email}.`);
  await expect(button).toBeDisabled();

  for (const near of [email.toUpperCase(), email.slice(0, -1), email.replace('example.com', 'example.org')]) {
    await field.fill(near);
    await expect(button).toBeDisabled();
  }
  await field.fill(`  ${email}  `);
  await expect(button).toBeEnabled();
  await button.click();

  await expect(page).toHaveURL(/\/login\?notice=deleted$/);
  await expect(page.getByRole('status')).toHaveText('Sua conta e todos os dados dela foram excluídos.');

  const own = await page.evaluate(async () => (await fetch('/api/auth/me', { credentials: 'same-origin' })).status);
  expect(own).toBe(401);

  // A copy of the cookie taken before the delete, replayed from outside the browser.
  const replay = await request.newContext({
    baseURL: baseURL!,
    extraHTTPHeaders: { Cookie: `${SESSION_COOKIE}=${session!.value}` },
  });
  expect((await replay.get('/api/auth/me')).status()).toBe(401);
  await replay.dispose();

  await page.goto('/');
  await expect(page).toHaveURL(/\/login$/);
});

/** 024 (023 decision 14): signed out in another tab, the delete is refused and says why. */
test('a delete from a tab whose session ended in another one says so, and deletes nothing', async ({ page }) => {
  const email = uniqueEmail('delete-account-ended');
  await devLogin(page, email, 'Sessão Encerrada');
  await createAccount(page, 'Ainda aqui');
  await page.goto('/settings');

  const other = await page.context().newPage();
  await other.goto('/');
  await other.getByRole('button', { name: 'Sair' }).click();
  await expect(other).toHaveURL(/\/login$/);
  await other.close();

  const zone = page.getByRole('region', { name: 'Excluir minha conta' });
  await zone.getByLabel('Digite seu e-mail para confirmar').fill(email);
  await zone.getByRole('button', { name: 'Excluir definitivamente' }).click();

  await expect(zone.getByRole('alert')).toHaveText('Sua sessão terminou. Entre de novo para excluir a conta.');
  await expect(page).toHaveURL(/\/settings$/);
  await expect(zone.getByRole('button', { name: 'Excluir definitivamente' })).toBeEnabled();

  // Signing in again finds the same account, with what it held.
  await devLogin(page, email, 'Sessão Encerrada');
  await page.goto('/accounts');
  await expect(page.getByRole('heading', { name: 'Ainda aqui', exact: true })).toBeVisible();
});
