import { expect, test } from '@playwright/test';

import { createAccount, createTransaction, devLogin, uniqueEmail } from './support';

/**
 * Spec E2E tests 1 to 3, against the real API and a real PostgreSQL.
 *
 * Every test signs in as its own fresh account, so the seeded categories are
 * there and nothing else is.
 */

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
