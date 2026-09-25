import { expect, request, test } from '@playwright/test';

import { devLogin, uniqueEmail } from './support';

const SESSION_COOKIE = '.AspNetCore.Identity.Application';

/** Spec 023 test 12. */
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
  await zone.getByLabel('Digite seu e-mail para confirmar').fill(email);
  await zone.getByRole('button', { name: 'Excluir definitivamente' }).click();

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
