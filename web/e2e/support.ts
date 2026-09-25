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

  // 015: a new account is selected, so its name becomes the detail's heading.
  await expect(page.getByRole('heading', { name, exact: true })).toBeVisible();
}

/**
 * An account's "Importar extrato" tab, reached the way a person reaches it (015): the
 * accounts, the account's card, the tab. Waits for the wizard's first step.
 */
export async function openImportTab(page: Page, account: string) {
  await page.goto('/accounts');
  await page.getByRole('link', { name: account, exact: true }).click();
  await expect(page.getByRole('heading', { name: account, exact: true })).toBeVisible();

  await page.getByRole('link', { name: 'Importar extrato' }).click();
  await expect(page.getByRole('heading', { name: '1. Arquivo' })).toBeVisible();
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

/**
 * A manual sync, so an asset has closes before its buy: a buy with no close yet has no
 * daily row until the next sync. Posted from the page so the browser sends the Origin
 * header the API's CSRF check requires, then polled until the run has finished. A 429 is
 * waited out: the rebuild after the previous run's sync can still hold the gate for a
 * moment after that run reads `Succeeded` (007 · CP5).
 */
export async function syncMarketData(page: Page) {
  const syncRunId = await page.evaluate(async () => {
    for (let attempt = 0; attempt < 60; attempt++) {
      const response = await fetch('/api/market-data/sync', {
        method: 'POST',
        credentials: 'same-origin',
        headers: { 'Content-Type': 'application/json' },
        body: '{}',
      });
      if (response.status === 202) {
        return ((await response.json()) as { syncRunId: string }).syncRunId;
      }
      if (response.status !== 429) {
        throw new Error(`sync answered ${response.status}`);
      }
      await new Promise((resolve) => setTimeout(resolve, 500));
    }
    throw new Error('the sync gate stayed busy for 30 s');
  });

  await expect
    .poll(
      () =>
        page.evaluate(async (id) => {
          const response = await fetch('/api/market-data/sync-runs', { credentials: 'same-origin' });
          const runs = (await response.json()) as { id: string; status: string }[];
          return runs.find((run) => run.id === id)?.status;
        }, syncRunId),
      { timeout: 15_000 },
    )
    .toBe('Succeeded');
}
