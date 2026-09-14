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
  await page.getByRole('button', { name: 'New account' }).click();
  await page.getByLabel('Name').fill(name);
  await page.getByLabel('Type').selectOption('Checking');
  await page.getByRole('button', { name: 'Create account' }).click();

  await expect(page.getByRole('cell', { name })).toBeVisible();
}

async function createTransaction(
  page: Page,
  values: { account: string; category: string; amount: string; date: string; description: string },
) {
  await page.goto('/transactions');
  await page.getByRole('button', { name: 'New transaction' }).click();

  await page.getByLabel('Account').selectOption({ label: values.account });
  await page.getByLabel('Category').selectOption({ label: values.category });
  await page.getByLabel('Amount').fill(values.amount);
  await page.getByLabel('Date').fill(values.date);
  await page.getByLabel('Description').fill(values.description);

  await page.getByRole('button', { name: 'Create transaction' }).click();
}

/** Spec E2E test 1. */
test('an expense and an income are created and shown with the correct sign', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-create'), 'Ada Lovelace');
  await createAccount(page, 'Nubank');

  await createTransaction(page, {
    account: 'Nubank',
    category: 'Food',
    amount: '42.90',
    date: '2026-09-13',
    description: 'Supermarket',
  });

  const expense = page.getByRole('row', { name: /Supermarket/ });
  await expect(expense).toBeVisible();

  // The user typed 42.90 and picked an expense category; the sign is the category's
  // doing, not the keyboard's.
  await expect(expense.getByTestId('amount')).toHaveText('−42,90');

  await createTransaction(page, {
    account: 'Nubank',
    category: 'Salary',
    amount: '3000.00',
    date: '2026-09-12',
    description: 'Salary September',
  });

  const income = page.getByRole('row', { name: /Salary September/ });
  await expect(income).toBeVisible();
  await expect(income.getByTestId('amount')).toHaveText('+3.000,00');
});

/** Spec E2E test 2. */
test('narrowing the date range narrows the list', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-filter'), 'Grace Hopper');
  await createAccount(page, 'Inter');

  await createTransaction(page, {
    account: 'Inter',
    category: 'Food',
    amount: '18.40',
    date: '2026-09-13',
    description: 'Inside the range',
  });

  await createTransaction(page, {
    account: 'Inter',
    category: 'Food',
    amount: '96.20',
    date: '2026-07-04',
    description: 'Outside the range',
  });

  await page.goto('/transactions');
  await page.getByLabel('From').fill('2026-09-01');
  await page.getByLabel('To').fill('2026-09-30');

  await expect(page.getByRole('row', { name: /Inside the range/ })).toBeVisible();
  await expect(page.getByRole('row', { name: /Outside the range/ })).toBeHidden();

  // A range that matches nothing says so, and says it differently from an account
  // with no transactions at all.
  await page.getByLabel('From').fill('2020-01-01');
  await page.getByLabel('To').fill('2020-12-31');

  await expect(page.getByText(/no transactions match/i)).toBeVisible();
});

/** Spec E2E test 3. */
test('editing an amount updates the list', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-edit'), 'Alan Turing');
  await createAccount(page, 'Cash');

  await createTransaction(page, {
    account: 'Cash',
    category: 'Leisure',
    amount: '55.90',
    date: '2026-09-10',
    description: 'Streaming subscription',
  });

  const row = page.getByRole('row', { name: /Streaming subscription/ });
  await expect(row.getByTestId('amount')).toHaveText('−55,90');

  await row.getByRole('button', { name: 'Edit' }).click();
  await page.getByLabel('Amount').fill('61.90');
  await page.getByRole('button', { name: 'Save transaction' }).click();

  await expect(
    page.getByRole('row', { name: /Streaming subscription/ }).getByTestId('amount'),
  ).toHaveText('−61,90');
});
