import { expect, test, type Page } from '@playwright/test';

import { devLogin, uniqueEmail } from './support';

/**
 * Spec E2E tests 1 to 3, against the real API and a real PostgreSQL.
 *
 * Every test signs in as its own fresh account, so the eight seeded categories are
 * there and nothing else is.
 */

async function createAccount(page: Page, name: string) {
  await page.goto('/accounts');
  await page.getByRole('button', { name: 'Nova conta' }).click();
  await page.getByLabel('Nome').fill(name);
  // By value, not label: the option reads "Conta corrente" but the wire contract is
  // still the English enum member.
  await page.getByLabel('Tipo').selectOption('Checking');
  await page.getByRole('button', { name: 'Criar conta' }).click();

  await expect(page.getByRole('cell', { name })).toBeVisible();
}

async function createTransaction(
  page: Page,
  values: { account: string; category: string; amount: string; date: string; description: string },
) {
  await page.goto('/transactions');
  await page.getByRole('button', { name: 'Novo lançamento' }).click();

  // Exact, because getByLabel matches substrings and the filter bar on this same
  // page is labelled "Filtrar por conta" and "Filtrar por categoria".
  await page.getByLabel('Conta', { exact: true }).selectOption({ label: values.account });
  await page.getByLabel('Categoria', { exact: true }).selectOption({ label: values.category });
  await page.getByLabel('Valor').fill(values.amount);
  await page.getByLabel('Data').fill(values.date);
  await page.getByLabel('Descrição').fill(values.description);

  await page.getByRole('button', { name: 'Criar lançamento' }).click();

  // Wait for the form to close, which it only does once the POST has succeeded.
  // Returning straight after the click lets the next navigation abort the request
  // in flight, and the row silently never exists.
  await expect(page.getByRole('button', { name: 'Criar lançamento' })).toBeHidden();
}

/** Spec E2E test 1. */
test('an expense and an income are created and shown with the correct sign', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-create'), 'Ada Lovelace');
  await createAccount(page, 'Nubank');

  await createTransaction(page, {
    account: 'Nubank',
    category: 'Alimentação',
    amount: '42.90',
    date: '2026-09-13',
    description: 'Supermercado',
  });

  const expense = page.getByRole('row', { name: /Supermercado/ });
  await expect(expense).toBeVisible();

  // The user typed 42.90 and picked an expense category; the sign is the category's
  // doing, not the keyboard's.
  await expect(expense.getByTestId('amount')).toHaveText('−42,90');

  await createTransaction(page, {
    account: 'Nubank',
    category: 'Salário',
    amount: '3000.00',
    date: '2026-09-12',
    description: 'Salário de setembro',
  });

  const income = page.getByRole('row', { name: /Salário de setembro/ });
  await expect(income).toBeVisible();
  await expect(income.getByTestId('amount')).toHaveText('+3.000,00');
});

/** Spec E2E test 2. */
test('narrowing the date range narrows the list', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-filter'), 'Grace Hopper');
  await createAccount(page, 'Inter');

  await createTransaction(page, {
    account: 'Inter',
    category: 'Alimentação',
    amount: '18.40',
    date: '2026-09-13',
    description: 'Dentro do período',
  });

  await createTransaction(page, {
    account: 'Inter',
    category: 'Alimentação',
    amount: '96.20',
    date: '2026-07-04',
    description: 'Fora do período',
  });

  await page.goto('/transactions');
  await page.getByLabel('De').fill('2026-09-01');
  await page.getByLabel('Até').fill('2026-09-30');

  await expect(page.getByRole('row', { name: /Dentro do período/ })).toBeVisible();
  await expect(page.getByRole('row', { name: /Fora do período/ })).toBeHidden();

  // A range that matches nothing says so, and says it differently from an account
  // with no transactions at all.
  await page.getByLabel('De').fill('2020-01-01');
  await page.getByLabel('Até').fill('2020-12-31');

  await expect(page.getByText(/nenhum lançamento corresponde/i)).toBeVisible();
});

/** Spec E2E test 3. */
test('editing an amount updates the list', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-edit'), 'Alan Turing');
  await createAccount(page, 'Cash');

  await createTransaction(page, {
    account: 'Cash',
    category: 'Lazer',
    amount: '55.90',
    date: '2026-09-10',
    description: 'Assinatura de streaming',
  });

  const row = page.getByRole('row', { name: /Assinatura de streaming/ });
  await expect(row.getByTestId('amount')).toHaveText('−55,90');

  await row.getByRole('button', { name: 'Editar' }).click();
  await page.getByLabel('Valor').fill('61.90');
  await page.getByRole('button', { name: 'Salvar lançamento' }).click();

  await expect(
    page.getByRole('row', { name: /Assinatura de streaming/ }).getByTestId('amount'),
  ).toHaveText('−61,90');
});
