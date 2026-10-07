import { expect, test, type Page } from '@playwright/test';

import {
  createAccount,
  createTransaction,
  devLogin,
  holdBack,
  importStep,
  showTransactionsBetween,
  uniqueEmail,
  uploadStatement,
} from './support';

/**
 * Spec 024: `/accounts` and an account's own page, against the real API and a real
 * PostgreSQL. Every test signs in as its own user, so it starts with no account.
 */

/** The account's card in the list beside the detail. */
function card(page: Page, name: string) {
  return page.getByRole('listitem').filter({ has: page.getByRole('link', { name, exact: true }) });
}

test('a new user creates the first account from the empty state', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-accounts-first'), 'Ada Lovelace');
  await page.goto('/accounts');

  await expect(page.getByText('Nenhuma conta ainda')).toBeVisible();
  await page.getByRole('button', { name: 'Criar a primeira conta' }).click();
  await expect(page.getByRole('heading', { name: 'Nova conta' })).toBeVisible();

  // Cancelar puts the empty state back and writes nothing.
  await page.getByRole('button', { name: 'Cancelar' }).click();
  await expect(page.getByText('Nenhuma conta ainda')).toBeVisible();

  await page.getByRole('button', { name: 'Criar a primeira conta' }).click();
  await page.getByLabel('Nome').fill('Nubank');
  await page.getByLabel('Saldo inicial').fill('1.500,00');
  await page.getByRole('button', { name: 'Criar conta' }).click();

  // The new account is selected, on its default tab.
  await expect(page).toHaveURL(/\/accounts\/[^/?]+$/);
  await expect(page.getByRole('heading', { name: 'Nubank', exact: true })).toBeVisible();
  await expect(page.getByTestId('account-balance')).toHaveText(/R\$\s*\+1\.500,00/);
  await expect(card(page, 'Nubank')).toContainText('Conta corrente · Sem importações');
  await expect(card(page, 'Nubank').getByTestId('amount')).toHaveText('+1.500,00');
  await expect(page.getByTestId('accounts-total')).toContainText('R$ 1.500,00');
  await expect(page.getByText('Nenhum lançamento nesta conta ainda.')).toBeVisible();
});

/**
 * 024: on a slow network the button is there before the list is. The list's answer is held
 * back, so Nova conta is clicked while /accounts is still about to open its first account.
 */
test('an account created before the list has loaded is the one selected', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-accounts-race'), 'Grace Hopper');
  await createAccount(page, 'Banco A');

  let release: () => void = () => {};
  const held = new Promise<void>((resolve) => (release = resolve));
  await page.route('**/api/accounts', async (route) => {
    if (route.request().method() === 'GET') {
      await held;
    }
    await route.continue();
  });

  await page.goto('/accounts');
  await page.getByRole('button', { name: 'Nova conta' }).click();
  release();
  await page.getByLabel('Nome').fill('Zeta');
  await page.getByRole('button', { name: 'Criar conta' }).click();

  await expect(page.getByRole('heading', { name: 'Zeta', exact: true })).toBeVisible();
});

/** 028: a create still in flight when its form is cancelled and another one is opened. */
test('an account created after its form was cancelled leaves the next form open', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-accounts-late'), 'Ada Lovelace');
  await createAccount(page, 'Banco A');
  const selected = page.url();

  const release = await holdBack(page, 'POST', '/api/accounts');
  const form = page.getByRole('region', { name: 'Nova conta' });
  await page.getByRole('button', { name: 'Nova conta' }).click();
  await form.getByLabel('Nome').fill('Zeta');
  await form.getByRole('button', { name: 'Criar conta' }).click();
  await form.getByRole('button', { name: 'Cancelar' }).click();
  await page.getByRole('button', { name: 'Nova conta' }).click();
  await form.getByLabel('Nome').fill('Ômega');
  release();

  // Zeta lands in the list, unselected, and its save is over once Criar conta is enabled again:
  // the form opened since still holds what was typed in it.
  await expect(card(page, 'Zeta')).toBeVisible();
  await expect(form.getByRole('button', { name: 'Criar conta' })).toBeEnabled();
  await expect(form.getByLabel('Nome')).toHaveValue('Ômega');
  expect(page.url()).toBe(selected);
  await form.getByRole('button', { name: 'Criar conta' }).click();
  await expect(page.getByRole('heading', { name: 'Ômega', exact: true })).toBeVisible();
});

test('an account the form or the API refuses says why, under the field', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-accounts-refused'), 'Grace Hopper');
  await createAccount(page, 'Nubank');

  await page.getByRole('button', { name: 'Nova conta' }).click();
  const form = page.getByRole('region', { name: 'Nova conta' });

  // A name in use: a 409, as one sentence above the form.
  await form.getByLabel('Nome').fill('Nubank');
  await form.getByRole('button', { name: 'Criar conta' }).click();
  await expect(form.getByRole('alert')).toHaveText("Já existe uma conta chamada 'Nubank'.");

  // A blank name and a currency that is not ISO 4217: a 400, every rule at once, each under
  // its field, and no sentence above the form (never the 400's English title).
  await form.getByLabel('Nome').fill('   ');
  await form.getByLabel('Moeda').fill('BR');
  await form.getByRole('button', { name: 'Criar conta' }).click();
  await expect(form.getByRole('alert').filter({ hasText: 'O nome é obrigatório.' })).toBeVisible();
  await expect(
    form.getByRole('alert').filter({ hasText: 'A moeda deve ser um código ISO 4217 de três letras maiúsculas.' }),
  ).toBeVisible();
  await expect(form.getByRole('alert')).toHaveCount(2);
  await expect(form).not.toContainText('One or more validation errors occurred.');

  // A balance that is not a number never leaves the page.
  await form.getByLabel('Nome').fill('Inter');
  await form.getByLabel('Moeda').fill('BRL');
  await form.getByLabel('Saldo inicial').fill('mil reais');
  await form.getByRole('button', { name: 'Criar conta' }).click();
  await expect(form.getByRole('alert').filter({ hasText: 'Informe um número.' })).toBeVisible();

  await form.getByLabel('Saldo inicial').fill('-250,75');
  await form.getByRole('button', { name: 'Criar conta' }).click();
  await expect(page.getByRole('heading', { name: 'Inter', exact: true })).toBeVisible();
  await expect(page.getByTestId('account-balance')).toHaveText(/R\$\s*−250,75/);
});

test('Detalhes da conta edits the name, the type and the opening balance', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-accounts-edit'), 'Alan Turing');
  await createAccount(page, 'Cartão');

  await page.getByRole('link', { name: 'Detalhes da conta' }).click();
  await expect(page).toHaveURL(/\?tab=details$/);
  const form = page.locator('form').filter({ has: page.getByRole('button', { name: 'Salvar conta' }) });
  await expect(form.getByLabel('Nome')).toHaveValue('Cartão');

  await form.getByLabel('Nome').fill('Cartão Nubank');
  await form.getByLabel('Tipo').selectOption('CreditCard');
  await form.getByLabel('Saldo inicial').fill('-1.234,56');
  await form.getByRole('button', { name: 'Salvar conta' }).click();

  await expect(page.getByRole('status')).toHaveText('Conta salva.');
  await expect(page.getByRole('heading', { name: 'Cartão Nubank', exact: true })).toBeVisible();
  await expect(page.getByTestId('account-balance')).toHaveText(/R\$\s*−1\.234,56/);
  await expect(card(page, 'Cartão Nubank')).toContainText('Cartão de crédito · Sem importações');
  await expect(page.getByTestId('accounts-total')).toContainText('-R$ 1.234,56');

  // Saved on the server: the form reopens on the new values, typed the way they are
  // entered, and saving again without touching them keeps the balance.
  await page.reload();
  await expect(form.getByLabel('Nome')).toHaveValue('Cartão Nubank');
  await expect(form.getByLabel('Tipo')).toHaveValue('CreditCard');
  await expect(form.getByLabel('Saldo inicial')).toHaveValue('-1234,56');
  await form.getByRole('button', { name: 'Salvar conta' }).click();
  await expect(page.getByRole('status')).toHaveText('Conta salva.');
  await page.reload();
  await expect(page.getByTestId('account-balance')).toHaveText(/R\$\s*−1\.234,56/);
});

test("the Lançamentos tab lists the account's own rows and leads to all of them", async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-accounts-tab'), 'Katherine Johnson');
  await createAccount(page, 'Nubank');
  await createAccount(page, 'Inter');
  await createTransaction(page, { account: 'Nubank', category: 'Alimentação', amount: '42', date: '2026-09-10', description: 'Mercado do Nubank' });
  await createTransaction(page, { account: 'Inter', category: 'Lazer', amount: '18', date: '2026-09-11', description: 'Cinema do Inter' });

  await page.goto('/accounts');
  await card(page, 'Nubank').getByRole('link', { name: 'Nubank', exact: true }).click();

  const rows = page.locator('[data-testid^="account-transaction-"]');
  await expect(rows).toHaveCount(1);
  await expect(rows).toContainText('Mercado do Nubank');
  await expect(rows).toContainText('10/09/2026 · Alimentação');
  await expect(rows.getByTestId('amount')).toHaveText('−42,00');

  await page.getByRole('link', { name: 'Ver todos os lançamentos da conta' }).click();

  // The whole account, whatever the dates: no month filter applied.
  await expect(page).toHaveURL(/\/transactions\?accountId=/);
  await expect(page.getByLabel('Filtrar por conta').locator('option:checked')).toHaveText('Nubank');
  await expect(page.getByLabel('De', { exact: true })).toHaveValue('');
  await expect(page.getByRole('row', { name: /Mercado do Nubank/ })).toBeVisible();
  await expect(page.getByRole('row', { name: /Cinema do Inter/ })).toHaveCount(0);
});

test('the tab is in the address: a reload and another account keep it', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-accounts-address'), 'Grace Hopper');
  await createAccount(page, 'Primeira');
  await createAccount(page, 'Segunda');

  // The old /import address, with nothing in review, opens the first account too, and an
  // opening balance left empty is zero.
  await page.goto('/import');
  await expect(page).toHaveURL(/\/accounts\/[^/?]+$/);
  await expect(page.getByRole('heading', { name: 'Primeira', exact: true })).toBeVisible();
  await expect(page.getByTestId('account-balance')).toHaveText(/R\$\s*\+0,00/);

  // `/accounts` alone opens the first account, in the tab asked for.
  await page.goto('/accounts?tab=details');
  await expect(page).toHaveURL(/\/accounts\/[^/?]+\?tab=details$/);
  const name = page.getByLabel('Nome');
  await expect(name).toHaveValue('Primeira');

  await page.reload();
  await expect(name).toHaveValue('Primeira');

  await card(page, 'Segunda').getByRole('link', { name: 'Segunda', exact: true }).click();
  await expect(page).toHaveURL(/\?tab=details$/);
  await expect(name).toHaveValue('Segunda');
});

test('an account is deleted only once nothing holds it', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-accounts-delete'), 'Ada Lovelace');
  await createAccount(page, 'Fica');
  await createAccount(page, 'Velha');
  await createTransaction(page, { account: 'Velha', category: 'Lazer', amount: '9,90', date: '2026-09-05', description: 'Último gasto' });

  const remove = async () => {
    await page.goto('/accounts');
    await card(page, 'Velha').getByRole('link', { name: 'Velha', exact: true }).click();
    await page.getByRole('link', { name: 'Detalhes da conta' }).click();
    await page.getByRole('button', { name: 'Excluir conta' }).click();
  };

  await remove();
  await expect(page.getByRole('alert')).toHaveText(
    "'Velha' ainda tem 1 lançamento(s). Mova ou exclua os lançamentos antes de excluir a conta.",
  );

  // The transaction goes; a statement left in review holds the account just the same.
  await page.goto('/transactions');
  await showTransactionsBetween(page, '2026-09-01', '2026-09-30');
  await page.getByRole('row', { name: /Último gasto/ }).getByRole('button', { name: 'Excluir' }).click();
  await expect(page.getByRole('row', { name: /Último gasto/ })).toHaveCount(0);
  await uploadStatement(page, 'Velha', 'extrato.ofx');
  await expect(page.getByRole('heading', { name: '3. Revisão' })).toBeVisible();

  await remove();
  await expect(page.getByRole('alert')).toHaveText(
    "'Velha' ainda tem 1 importação(ões) no histórico. Desfaça ou descarte as importações antes de excluir a conta.",
  );

  await page.getByRole('link', { name: 'Importar extrato' }).click();
  await page.getByRole('button', { name: 'Continuar a revisão' }).click();
  await importStep(page).getByRole('button', { name: 'Descartar' }).click();
  await expect(page.getByText('Nenhuma importação nesta conta ainda.')).toBeVisible();

  await remove();
  await expect(page).toHaveURL(/\/accounts\/[^/?]+$/);
  await expect(page.getByRole('heading', { name: 'Fica', exact: true })).toBeVisible();
  await expect(card(page, 'Velha')).toHaveCount(0);
});
