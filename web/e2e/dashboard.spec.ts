import { expect, test, type Page } from '@playwright/test';

import { createAccount, createTransaction, devLogin, uniqueEmail } from './support';

/**
 * Spec 005 E2E tests 24 to 26, against the real API and a real PostgreSQL.
 *
 * Every test signs in as a brand-new user, so the only rows are the ones it creates,
 * and the seeded categories include Transferência (spec 005 integration test 19).
 */

/**
 * Today as the browser sees it, `YYYY-MM-DD`. The dashboard's current month is the
 * local one, so the transactions have to land in it too.
 */
function today() {
  const now = new Date();
  const month = String(now.getMonth() + 1).padStart(2, '0');
  const day = String(now.getDate()).padStart(2, '0');

  return `${now.getFullYear()}-${month}-${day}`;
}

/** The balance row of the account called `name`; its test id carries the account id. */
function accountBalance(page: Page, name: string) {
  return page.locator('[data-testid^="account-balance-"]').filter({ hasText: name }).getByTestId('amount');
}

function stat(page: Page, testId: string) {
  return page.getByTestId(testId).getByTestId('amount');
}

/** Spec E2E test 24. */
test('signing in lands on the dashboard, empty until there is an account', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-dashboard'), 'Ada Lovelace');

  await page.goto('/');

  await expect(page).toHaveURL(/\/$/);
  await expect(page.getByRole('heading', { name: 'Início' })).toBeVisible();
  await expect(page.getByTestId('current-user')).toHaveText('Ada Lovelace');

  // A brand-new user has nothing recorded: the empty state, not a page of zeros.
  await expect(page.getByTestId('dashboard-empty')).toBeVisible();
  await expect(page.getByTestId('total-balance')).toBeHidden();

  await createAccount(page, 'Nubank', '1.500,00');
  await page.goto('/');

  await expect(page.getByTestId('dashboard-empty')).toBeHidden();
  await expect(stat(page, 'total-balance')).toHaveText('+1.500,00');
  await expect(accountBalance(page, 'Nubank')).toHaveText('+1.500,00');
  await expect(page.getByTestId('selected-month')).toBeVisible();
  await expect(stat(page, 'month-income')).toHaveText('+0,00');
  await expect(stat(page, 'month-expense')).toHaveText('+0,00');
  await expect(stat(page, 'month-net')).toHaveText('+0,00');
  await expect(page.getByTestId('monthly-chart')).toBeVisible();
  await expect(page.getByTestId('category-breakdown')).toBeVisible();
  await expect(page.getByTestId('recent-transactions')).toBeVisible();
});

/** Spec E2E test 25. */
test('an expense lowers the total balance and shows in the month', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-dashboard-expense'), 'Grace Hopper');
  await createAccount(page, 'Inter', '1.000,00');

  await page.goto('/');
  await expect(stat(page, 'total-balance')).toHaveText('+1.000,00');
  await expect(stat(page, 'month-expense')).toHaveText('+0,00');

  await createTransaction(page, {
    account: 'Inter',
    category: 'Alimentação',
    amount: '42.90',
    date: today(),
    description: 'Supermercado do painel',
  });

  await page.goto('/');

  await expect(stat(page, 'total-balance')).toHaveText('+957,10');
  await expect(accountBalance(page, 'Inter')).toHaveText('+957,10');
  await expect(stat(page, 'month-expense')).toHaveText('−42,90');
  await expect(stat(page, 'month-net')).toHaveText('−42,90');
  await expect(page.getByTestId('recent-transactions')).toContainText('Supermercado do painel');
});

/** Spec E2E test 26. */
test('a Transferência moves the balance and leaves the month totals alone', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-dashboard-transfer'), 'Alan Turing');
  await createAccount(page, 'Itaú', '1.000,00');

  await createTransaction(page, {
    account: 'Itaú',
    category: 'Alimentação',
    amount: '100.00',
    date: today(),
    description: 'Feira',
  });

  await page.goto('/');
  await expect(accountBalance(page, 'Itaú')).toHaveText('+900,00');
  await expect(stat(page, 'month-income')).toHaveText('+0,00');
  await expect(stat(page, 'month-expense')).toHaveText('−100,00');

  // Transferência is seeded for every new user; selecting it by label fails the test
  // if it is missing. The form asks the direction only for a Transfer category.
  await createTransaction(page, {
    account: 'Itaú',
    category: 'Transferência',
    direction: 'Saída',
    amount: '300.00',
    date: today(),
    description: 'Pagamento da fatura',
  });
  await createTransaction(page, {
    account: 'Itaú',
    category: 'Transferência',
    direction: 'Entrada',
    amount: '50.00',
    date: today(),
    description: 'Resgate da poupança',
  });

  await page.goto('/');

  await expect(accountBalance(page, 'Itaú')).toHaveText('+650,00');
  await expect(stat(page, 'total-balance')).toHaveText('+650,00');
  await expect(stat(page, 'month-income')).toHaveText('+0,00');
  await expect(stat(page, 'month-expense')).toHaveText('−100,00');
  await expect(stat(page, 'month-net')).toHaveText('−100,00');

  // The breakdown has the expense category and never the transfer.
  const rows = page.getByTestId('category-breakdown').getByTestId('category-row');
  await expect(rows).toHaveCount(1);
  await expect(rows).toContainText('Alimentação');
});
