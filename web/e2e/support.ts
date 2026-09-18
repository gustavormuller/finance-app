import { expect, type Page } from '@playwright/test';

/**
 * Establishes a session through the development-only login endpoint, because
 * Playwright cannot complete a real Google flow.
 *
 * Issued as a fetch from inside the page rather than through Playwright's request
 * context, so the browser is the one doing it: it sends the Origin header the API's
 * CSRF check requires, and it stores the Secure session cookie under the same rules
 * the real callback relies on.
 */
export async function devLogin(page: Page, email: string, displayName: string) {
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
export function uniqueEmail(prefix: string) {
  return `${prefix}-${crypto.randomUUID()}@example.com`;
}

/** An account by way of the real screen, so every spec starts from a state a person could reach. */
export async function createAccount(page: Page, name: string) {
  await page.goto('/accounts');
  await page.getByRole('button', { name: 'Nova conta' }).click();
  await page.getByLabel('Nome').fill(name);
  await page.getByLabel('Tipo').selectOption('Checking');
  await page.getByRole('button', { name: 'Criar conta' }).click();

  await expect(page.getByRole('cell', { name })).toBeVisible();
}
