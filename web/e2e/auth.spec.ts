import { expect, test, type Page } from '@playwright/test';

/**
 * Establishes a session through the development-only login endpoint, because
 * Playwright cannot complete a real Google flow.
 *
 * Issued as a fetch from inside the page rather than through Playwright's request
 * context, so the browser is the one doing it: it sends the Origin header the API's
 * CSRF check requires, and it stores the Secure session cookie under the same rules
 * the real callback relies on.
 */
async function devLogin(page: Page, email: string, displayName: string) {
  await page.goto('/login');

  const status = await page.evaluate(
    async (account) => {
      const response = await fetch('/api/auth/dev-login', {
        method: 'POST',
        credentials: 'same-origin',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(account),
      });

      return response.status;
    },
    { email, displayName },
  );

  expect(status).toBe(204);
}

/** The dev database persists across runs, so every test brings its own account. */
function uniqueEmail(prefix: string) {
  return `${prefix}-${crypto.randomUUID()}@example.com`;
}

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
