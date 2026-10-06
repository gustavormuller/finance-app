import { expect, test, type Page } from '@playwright/test';

import {
  createAccount,
  createTransaction,
  devLogin,
  localMonthDay,
  localToday,
  showDashboardMonth,
  uniqueEmail,
} from './support';

/**
 * Spec 005 E2E tests 24 to 26, against the real API and a real PostgreSQL.
 *
 * Every test signs in as a brand-new user, so the only rows are the ones it creates,
 * and the seeded categories include Transferência (spec 005 integration test 19).
 *
 * The current month and the hero's chips are relative to today by definition, so those
 * tests date their rows from today (`localToday`, `localMonthDay`); the rest use fixed
 * dates and page the month selector back to them.
 */

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
  const empty = page.getByTestId('dashboard-empty');
  await expect(empty).toBeVisible();
  await expect(page.getByTestId('total-balance')).toBeHidden();

  // 024: its two ways out.
  await empty.getByRole('link', { name: 'Registrar um lançamento' }).click();
  await expect(page).toHaveURL(/\/transactions$/);
  await page.goto('/');
  await empty.getByRole('link', { name: 'Importar um extrato' }).click();
  await expect(page).toHaveURL(/\/accounts$/);

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

  // 024: an account with no transaction has nothing recent, and the way to the list.
  const recent = page.getByTestId('recent-transactions');
  await expect(recent).toContainText('Nenhum lançamento ainda.');
  await recent.getByRole('link', { name: 'Ver todos os lançamentos' }).click();
  await expect(page).toHaveURL(/\/transactions$/);
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
    date: localToday(),
    description: 'Supermercado do painel',
  });

  await page.goto('/');

  await expect(stat(page, 'total-balance')).toHaveText('+957,10');
  await expect(accountBalance(page, 'Inter')).toHaveText('+957,10');
  await expect(stat(page, 'month-expense')).toHaveText('−42,90');
  await expect(stat(page, 'month-net')).toHaveText('−42,90');
  await expect(page.getByTestId('recent-transactions')).toContainText('Supermercado do painel');
});

/**
 * Spec 018 E2E: reads stay fresh for 30 s, and a write must not be hidden by them. The
 * transaction form invalidates only the transactions; coming back to the dashboard through
 * the sidebar, with the query cache intact, still shows the new balance.
 */
test('an expense added after the dashboard was read shows on it after a client-side navigation', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-dashboard-fresh'), 'Grace Hopper');
  await createAccount(page, 'Inter', '1.000,00');

  await page.goto('/');
  await expect(stat(page, 'total-balance')).toHaveText('+1.000,00');

  // Through the sidebar, not page.goto: the query cache survives, as it does for a person.
  const nav = page.getByRole('navigation', { name: 'Principal' });
  await nav.getByRole('link', { name: 'Lançamentos', exact: true }).click();
  await page.getByRole('button', { name: 'Novo lançamento' }).click();
  await page.getByLabel('Conta', { exact: true }).selectOption({ label: 'Inter' });
  await page.getByLabel('Categoria', { exact: true }).selectOption({ label: 'Alimentação' });
  await page.getByLabel('Valor').fill('42.90');
  await page.getByLabel('Data').fill(localToday());
  await page.getByLabel('Descrição').fill('Padaria sem recarregar');
  await page.getByRole('button', { name: 'Criar lançamento' }).click();
  await expect(page.getByRole('button', { name: 'Criar lançamento' })).toBeHidden();

  await nav.getByRole('link', { name: 'Início', exact: true }).click();

  await expect(stat(page, 'total-balance')).toHaveText('+957,10');
  await expect(stat(page, 'month-expense')).toHaveText('−42,90');
  await expect(page.getByTestId('recent-transactions')).toContainText('Padaria sem recarregar');
});

/** Spec E2E test 26. */
test('a Transferência moves the balance and leaves the month totals alone', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-dashboard-transfer'), 'Alan Turing');
  await createAccount(page, 'Itaú', '1.000,00');

  await createTransaction(page, {
    account: 'Itaú',
    category: 'Alimentação',
    amount: '100.00',
    date: localToday(),
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
    date: localToday(),
    description: 'Pagamento da fatura',
  });
  await createTransaction(page, {
    account: 'Itaú',
    category: 'Transferência',
    direction: 'Entrada',
    amount: '50.00',
    date: localToday(),
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

/**
 * Spec 014 E2E test 14: last month ends at 900,00 and this month is at 857,10, with
 * nothing invested, so net worth and Em contas agree and the month's change is the
 * expense. Twelve months back there is no history, so that chip stays hidden.
 */
test('the hero shows net worth, its change over the month and the sparkline', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-net-worth'), 'Katherine Johnson');
  await createAccount(page, 'Caixa', '1.000,00');

  await createTransaction(page, {
    account: 'Caixa',
    category: 'Alimentação',
    amount: '100.00',
    date: localMonthDay(1),
    description: 'Mercado do mês passado',
  });
  await createTransaction(page, {
    account: 'Caixa',
    category: 'Alimentação',
    amount: '42.90',
    date: localToday(),
    description: 'Padaria',
  });

  await page.goto('/');

  await expect(page.getByRole('heading', { name: 'Patrimônio · contas + investimentos' })).toBeVisible();
  await expect(stat(page, 'net-worth-total')).toHaveText('+857,10');
  await expect(stat(page, 'total-balance')).toHaveText('+857,10');
  await expect(page.getByTestId('net-worth-change-month')).toHaveText('1 mês −42,90');
  await expect(page.getByTestId('net-worth-change-twelve')).toBeHidden();
  await expect(page.getByTestId('hero-invested')).toBeHidden();
  await expect(page.getByTestId('net-worth-chart')).toBeVisible();
});

/**
 * 024: with fourteen months of history, last December and the month a year ago are both
 * in the series, whatever today is. Nothing moved between them and last month, so every
 * chip is this month's expense.
 */
test("with more than a year of history the hero also shows the year's and twelve months' change", async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-net-worth-year'), 'Katherine Johnson');
  await createAccount(page, 'Caixa', '1.000,00');
  await createTransaction(page, { account: 'Caixa', category: 'Moradia', amount: '100', date: localMonthDay(14), description: 'Aluguel antigo' });
  await createTransaction(page, { account: 'Caixa', category: 'Alimentação', amount: '42.90', date: localToday(), description: 'Padaria' });

  await page.goto('/');

  await expect(stat(page, 'net-worth-total')).toHaveText('+857,10');
  await expect(page.getByTestId('net-worth-change-month')).toHaveText('1 mês −42,90');
  await expect(page.getByTestId('net-worth-change-year')).toHaveText('No ano −42,90');
  await expect(page.getByTestId('net-worth-change-twelve')).toHaveText('12 meses −42,90');
});

/** 024: what each account row says beyond its balance. */
test('a credit card in debt counts against the total, an account in dollars stays out of it', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-dashboard-accounts'), 'Grace Hopper');
  await createAccount(page, 'Corrente', '1.000,00');
  await createAccount(page, 'Cartão', '-500,00', { type: 'CreditCard' });
  await createAccount(page, 'Conta em dólar', '100,00', { currency: 'USD' });

  await page.goto('/');

  await expect(stat(page, 'total-balance')).toHaveText('+500,00');
  await expect(stat(page, 'net-worth-total')).toHaveText('+500,00');
  await expect(accountBalance(page, 'Cartão')).toHaveText('−500,00');
  await expect(page.locator('[data-testid^="account-balance-"]').filter({ hasText: 'Cartão' })).toContainText('Cartão de crédito');
  const dollars = page.locator('[data-testid^="account-balance-"]').filter({ hasText: 'Conta em dólar' });
  await expect(dollars).toContainText('Conta corrente · USD, fora do total');
  await expect(dollars.getByTestId('amount')).toHaveText('+100,00');
});

/**
 * 024: a fixed month, reached with the selector: its totals, how much of the income it
 * kept, and the breakdown on both sides. A month with nothing says so.
 */
test('the month selector shows a past month, what it kept, and both sides of its breakdown', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-dashboard-month'), 'Alan Turing');
  await createAccount(page, 'Inter');
  await createTransaction(page, { account: 'Inter', category: 'Salário', amount: '3000', date: '2026-08-05', description: 'Salário de agosto' });
  await createTransaction(page, { account: 'Inter', category: 'Alimentação', amount: '600', date: '2026-08-10', description: 'Mercado do mês' });
  await createTransaction(page, { account: 'Inter', category: 'Lazer', amount: '150', date: '2026-08-12', description: 'Show' });
  await createTransaction(page, { account: 'Inter', category: 'Outras receitas', amount: '500', date: '2026-08-20', description: 'Venda de livros' });

  await page.goto('/');

  // Nothing worth paging to after the current month.
  await expect(page.getByRole('button', { name: 'Próximo mês' })).toBeDisabled();
  await showDashboardMonth(page, '2026-08', 'agosto de 2026');
  await expect(page.getByRole('button', { name: 'Próximo mês' })).toBeEnabled();

  await expect(stat(page, 'month-income')).toHaveText('+3.500,00');
  await expect(stat(page, 'month-expense')).toHaveText('−750,00');
  await expect(stat(page, 'month-net')).toHaveText('+2.750,00');
  await expect(page.getByText('79% do que entrou')).toBeVisible();

  const breakdown = page.getByTestId('category-breakdown');
  await expect(breakdown).toContainText('Por categoria · agosto de 2026');
  const rows = breakdown.getByTestId('category-row');
  await expect(rows).toHaveCount(2);
  await expect(rows.nth(0)).toContainText(/Alimentação\s*80,0%\s*−600,00/);
  await expect(rows.nth(1)).toContainText(/Lazer\s*20,0%\s*−150,00/);

  await breakdown.getByRole('button', { name: 'Receitas' }).click();
  await expect(breakdown.getByRole('button', { name: 'Receitas' })).toHaveAttribute('aria-pressed', 'true');
  await expect(rows.nth(0)).toContainText(/Salário\s*85,7%\s*\+3\.000,00/);
  await expect(rows.nth(1)).toContainText(/Outras receitas\s*14,3%\s*\+500,00/);

  await page.getByRole('button', { name: 'Mês anterior' }).click();
  await expect(page.getByTestId('selected-month')).toHaveText('julho de 2026');
  await expect(breakdown).toContainText('Nada registrado neste mês.');
  await expect(stat(page, 'month-income')).toHaveText('+0,00');
  await expect(page.getByText(/do que entrou/)).toHaveCount(0);

  await page.getByRole('button', { name: 'Próximo mês' }).click();
  await expect(page.getByTestId('selected-month')).toHaveText('agosto de 2026');
  await expect(stat(page, 'month-net')).toHaveText('+2.750,00');
});
