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

/**
 * An account by way of the real screen, so every spec starts from a state a person could reach.
 * `openingBalance` is typed as a person would, e.g. `1.000,00`; omitted, the field stays empty (0).
 */
export async function createAccount(page: Page, name: string, openingBalance?: string) {
  await page.goto('/accounts');
  await page.getByRole('button', { name: 'Nova conta' }).click();
  await page.getByLabel('Nome').fill(name);
  // By value, not label: the option reads "Conta corrente" but the wire contract is
  // still the English enum member.
  await page.getByLabel('Tipo').selectOption('Checking');
  if (openingBalance !== undefined) {
    await page.getByLabel('Saldo inicial').fill(openingBalance);
  }
  await page.getByRole('button', { name: 'Criar conta' }).click();

  await expect(page.getByRole('cell', { name })).toBeVisible();
}

/**
 * A transaction by way of the real form. `direction` answers the Saída/Entrada question
 * the form asks only for a Transfer category.
 */
export async function createTransaction(
  page: Page,
  values: {
    account: string;
    category: string;
    amount: string;
    date: string;
    description: string;
    direction?: 'Saída' | 'Entrada';
  },
) {
  await page.goto('/transactions');
  await page.getByRole('button', { name: 'Novo lançamento' }).click();

  // Exact, because getByLabel matches substrings and the filter bar on this same
  // page is labelled "Filtrar por conta" and "Filtrar por categoria".
  await page.getByLabel('Conta', { exact: true }).selectOption({ label: values.account });
  await page.getByLabel('Categoria', { exact: true }).selectOption({ label: values.category });
  if (values.direction) {
    await page.getByRole('radio', { name: values.direction, exact: true }).check();
  }
  await page.getByLabel('Valor').fill(values.amount);
  await page.getByLabel('Data').fill(values.date);
  await page.getByLabel('Descrição').fill(values.description);

  await page.getByRole('button', { name: 'Criar lançamento' }).click();

  // Wait for the form to close, which it only does once the POST has succeeded.
  // Returning straight after the click lets the next navigation abort the request
  // in flight, and the row silently never exists.
  await expect(page.getByRole('button', { name: 'Criar lançamento' })).toBeHidden();
}
