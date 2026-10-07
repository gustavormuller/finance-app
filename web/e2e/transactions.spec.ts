import { expect, test, type Route } from '@playwright/test';

import {
  commitImport,
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
  await showTransactionsBetween(page, '2026-09-01', '2026-09-30');

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
  await showTransactionsBetween(page, '2026-09-01', '2026-09-30');

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

/** Spec E2E test 3, and 024: the form opens on the row's values, and on an edit too the sign is the category's. */
test('editing a transaction updates its row', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-edit'), 'Alan Turing');
  await createAccount(page, 'Cash');

  await createTransaction(page, {
    account: 'Cash',
    category: 'Lazer',
    amount: '55.90',
    date: '2026-09-10',
    description: 'Assinatura de streaming',
  });
  await showTransactionsBetween(page, '2026-09-01', '2026-09-30');

  const row = page.getByRole('row', { name: /Assinatura de streaming/ });
  await expect(row.getByTestId('amount')).toHaveText('−55,90');

  await row.getByRole('button', { name: 'Editar' }).click();
  // Unsigned, as typed: the sign is the category's.
  await expect(page.getByLabel('Valor')).toHaveValue('55.90');
  await expect(page.getByLabel('Data')).toHaveValue('2026-09-10');
  await page.getByLabel('Valor').fill('61.90');
  await page.getByLabel('Descrição').fill('Assinatura anual de streaming');
  await page.getByRole('button', { name: 'Salvar lançamento' }).click();
  await expect(page.getByRole('button', { name: 'Salvar lançamento' })).toBeHidden();

  const edited = page.getByRole('row', { name: /Assinatura anual de streaming/ });
  await expect(edited.getByTestId('amount')).toHaveText('−61,90');
  await expect(edited).toContainText('Lazer');

  // Filed under an income category, the same typed amount arrives instead of leaving.
  await edited.getByRole('button', { name: 'Editar' }).click();
  await page.getByLabel('Categoria', { exact: true }).selectOption({ label: 'Outras receitas' });
  await page.getByRole('button', { name: 'Salvar lançamento' }).click();

  await expect(edited.getByTestId('amount')).toHaveText('+61,90');
  await expect(edited).toContainText('Outras receitas');

  // A transfer that arrived opens on its own direction.
  await createTransaction(page, {
    account: 'Cash',
    category: 'Transferência',
    direction: 'Entrada',
    amount: '200',
    date: '2026-09-11',
    description: 'Resgate',
  });
  await showTransactionsBetween(page, '2026-09-01', '2026-09-30');
  await page.getByRole('row', { name: /Resgate/ }).getByRole('button', { name: 'Editar' }).click();
  await expect(page.getByRole('radio', { name: 'Entrada' })).toBeChecked();
});

/** 024: deleting asks nothing and takes only that row. */
test('deleting a transaction removes it from the list', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-delete'), 'Grace Hopper');
  await createAccount(page, 'Nubank');
  await createTransaction(page, { account: 'Nubank', category: 'Alimentação', amount: '20', date: '2026-09-03', description: 'Lanche que fica' });
  await createTransaction(page, { account: 'Nubank', category: 'Lazer', amount: '35', date: '2026-09-04', description: 'Cinema duplicado' });
  await showTransactionsBetween(page, '2026-09-01', '2026-09-30');

  await page.getByRole('row', { name: /Cinema duplicado/ }).getByRole('button', { name: 'Excluir' }).click();

  await expect(page.getByRole('row', { name: /Cinema duplicado/ })).toHaveCount(0);
  await expect(page.getByRole('row', { name: /Lanche que fica/ })).toBeVisible();

  // Gone on the server, not only from the screen.
  await page.reload();
  await showTransactionsBetween(page, '2026-09-01', '2026-09-30');
  await expect(page.getByRole('row', { name: /Lanche que fica/ })).toBeVisible();
  await expect(page.getByRole('row', { name: /Cinema duplicado/ })).toHaveCount(0);
});

/** 024: the account and category filters, alone and together. */
test('the account and category filters narrow the list', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-filters'), 'Katherine Johnson');
  await createAccount(page, 'Nubank');
  await createAccount(page, 'Inter');
  await createTransaction(page, { account: 'Nubank', category: 'Alimentação', amount: '10', date: '2026-09-10', description: 'Mercado no Nubank' });
  await createTransaction(page, { account: 'Nubank', category: 'Lazer', amount: '20', date: '2026-09-11', description: 'Cinema no Nubank' });
  await createTransaction(page, { account: 'Inter', category: 'Alimentação', amount: '30', date: '2026-09-12', description: 'Mercado no Inter' });
  await showTransactionsBetween(page, '2026-09-01', '2026-09-30');

  const row = (description: string) => page.getByRole('row', { name: new RegExp(description) });
  const shown = async (...descriptions: string[]) => {
    for (const description of descriptions) {
      await expect(row(description)).toBeVisible();
    }
  };

  await shown('Mercado no Nubank', 'Cinema no Nubank', 'Mercado no Inter');

  await page.getByLabel('Filtrar por conta').selectOption({ label: 'Nubank' });
  await shown('Mercado no Nubank', 'Cinema no Nubank');
  await expect(row('Mercado no Inter')).toHaveCount(0);

  await page.getByLabel('Filtrar por categoria').selectOption({ label: 'Alimentação' });
  await shown('Mercado no Nubank');
  await expect(row('Cinema no Nubank')).toHaveCount(0);
  await expect(row('Mercado no Inter')).toHaveCount(0);

  await page.getByLabel('Filtrar por conta').selectOption({ label: 'Todas as contas' });
  await shown('Mercado no Nubank', 'Mercado no Inter');
  await expect(row('Cinema no Nubank')).toHaveCount(0);

  // A category with nothing in it is a filter that matched nothing, not an empty ledger.
  await page.getByLabel('Filtrar por categoria').selectOption({ label: 'Moradia' });
  await expect(page.getByText('Nenhum lançamento corresponde a este filtro')).toBeVisible();
});

/**
 * 024: the list opens on the browser's current month, which is a filter, so an empty
 * month reads differently from a ledger with nothing in it at all.
 */
test('the list opens on the current month, and an empty ledger says so', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-empty-list'), 'Ada Lovelace');
  await page.goto('/transactions');

  const month = await page.evaluate(() => {
    const now = new Date();
    const pad = (value: number) => String(value).padStart(2, '0');
    const last = new Date(now.getFullYear(), now.getMonth() + 1, 0).getDate();
    const prefix = `${now.getFullYear()}-${pad(now.getMonth() + 1)}`;
    return { from: `${prefix}-01`, to: `${prefix}-${pad(last)}` };
  });
  await expect(page.getByLabel('De', { exact: true })).toHaveValue(month.from);
  await expect(page.getByLabel('Até', { exact: true })).toHaveValue(month.to);
  await expect(page.getByText('Nenhum lançamento corresponde a este filtro')).toBeVisible();

  await page.getByLabel('De', { exact: true }).fill('');
  await page.getByLabel('Até', { exact: true }).fill('');

  await expect(page.getByText('Nenhum lançamento ainda')).toBeVisible();
  await expect(page.getByText('Registre o primeiro para começar a acompanhar para onde o dinheiro vai.')).toBeVisible();
});

/** 024: what the form refuses before a round trip, and Cancelar. */
test('the form checks what it can before sending, and Cancelar writes nothing', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-form-checks'), 'Grace Hopper');
  await createAccount(page, 'Nubank');
  await page.goto('/transactions');
  await showTransactionsBetween(page, '2026-09-01', '2026-09-30');

  await page.getByRole('button', { name: 'Novo lançamento' }).click();
  await page.getByLabel('Conta', { exact: true }).selectOption('');
  await page.getByRole('button', { name: 'Criar lançamento' }).click();

  for (const message of ['Escolha uma conta.', 'Escolha uma categoria.', 'Informe um valor.', 'Escolha uma data.', 'Informe uma descrição.']) {
    await expect(page.getByRole('alert').filter({ hasText: message })).toBeVisible();
  }

  await page.getByLabel('Valor').fill('0,00');
  await page.getByRole('button', { name: 'Criar lançamento' }).click();
  await expect(page.getByRole('alert').filter({ hasText: 'O valor não pode ser zero.' })).toBeVisible();

  await page.getByLabel('Valor').fill('doze');
  await page.getByRole('button', { name: 'Criar lançamento' }).click();
  await expect(page.getByRole('alert').filter({ hasText: 'Informe um número.' })).toBeVisible();

  // The categories come grouped by kind, and only a Transferência asks for a direction,
  // Saída unless told otherwise.
  const category = page.getByLabel('Categoria', { exact: true });
  expect(await category.locator('optgroup').evaluateAll((groups) => groups.map((group) => group.getAttribute('label')))).toEqual([
    'Receita',
    'Despesa',
    'Transferência',
  ]);
  await category.selectOption({ label: 'Transferência' });
  await expect(page.getByRole('radio', { name: 'Saída' })).toBeChecked();
  await category.selectOption({ label: 'Lazer' });
  await expect(page.getByRole('radio', { name: 'Saída' })).toHaveCount(0);

  await page.getByLabel('Conta', { exact: true }).selectOption({ label: 'Nubank' });
  await page.getByLabel('Valor').fill('12,5');
  await page.getByLabel('Data').fill('2026-09-20');
  await page.getByLabel('Descrição').fill('Nunca enviado');
  await page.getByRole('button', { name: 'Cancelar' }).click();

  await expect(page.getByRole('button', { name: 'Criar lançamento' })).toBeHidden();
  await expect(page.getByText('Nenhum lançamento corresponde a este filtro')).toBeVisible();

  // The same values, sent this time: a comma is a decimal separator, as the app writes money.
  await createTransaction(page, { account: 'Nubank', category: 'Lazer', amount: '12,5', date: '2026-09-20', description: 'Enviado' });
  // A minus typed anyway does not turn an income into an expense: the sign is the category's.
  await createTransaction(page, { account: 'Nubank', category: 'Outras receitas', amount: '-100', date: '2026-09-21', description: 'Reembolso' });
  await showTransactionsBetween(page, '2026-09-01', '2026-09-30');
  await expect(page.getByRole('row', { name: /Enviado/ }).getByTestId('amount')).toHaveText('−12,50');
  await expect(page.getByRole('row', { name: /Reembolso/ }).getByTestId('amount')).toHaveText('+100,00');
});

/** 024: a rule only the API knows, refused with the API's own sentence. */
test('a date the API refuses is explained in Portuguese', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-refused-date'), 'Alan Turing');
  await createAccount(page, 'Nubank');
  await page.goto('/transactions');
  await page.getByRole('button', { name: 'Novo lançamento' }).click();
  await page.getByLabel('Conta', { exact: true }).selectOption({ label: 'Nubank' });
  await page.getByLabel('Categoria', { exact: true }).selectOption({ label: 'Alimentação' });
  await page.getByLabel('Valor').fill('10');
  await page.getByLabel('Data').fill('1899-12-31');
  await page.getByLabel('Descrição').fill('Antes de 1900');
  await page.getByRole('button', { name: 'Criar lançamento' }).click();

  await expect(page.getByRole('alert')).toContainText('A data deve estar entre 01/01/1900 e');
  await expect(page.getByRole('alert')).not.toContainText('One or more validation errors occurred.');
});

/**
 * 028: refusals that bring no sentence of their own, and requests that never reach the API.
 * The 404 is real, for a row deleted in a second tab; the rest are answered in this page only.
 */
test('a refusal that comes without a sentence still reads in Portuguese', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-bare-refusals'), 'Grace Hopper');
  await createAccount(page, 'Nubank');
  await createTransaction(page, { account: 'Nubank', category: 'Lazer', amount: '20', date: '2026-09-08', description: 'Teatro' });
  await createTransaction(page, { account: 'Nubank', category: 'Alimentação', amount: '35', date: '2026-09-09', description: 'Mercado' });
  await showTransactionsBetween(page, '2026-09-01', '2026-09-30');
  const alert = page.getByRole('alert');

  await page.getByRole('row', { name: /Teatro/ }).getByRole('button', { name: 'Editar' }).click();
  const other = await page.context().newPage();
  await other.goto('/transactions');
  await showTransactionsBetween(other, '2026-09-01', '2026-09-30');
  await other.getByRole('row', { name: /Teatro/ }).getByRole('button', { name: 'Excluir' }).click();
  await expect(other.getByRole('row', { name: /Teatro/ })).toHaveCount(0);
  await other.close();
  await page.getByLabel('Descrição').fill('Teatro municipal');
  await page.getByRole('button', { name: 'Salvar lançamento' }).click();
  await expect(alert).toHaveText('Este item não existe mais; talvez tenha sido excluído em outra aba. Recarregue a página.');
  await page.getByRole('button', { name: 'Cancelar' }).click();
  await expect(alert).toHaveCount(0);

  const network = 'Não foi possível falar com o servidor. Confira a conexão e tente de novo.';
  const exports = (url: URL) => url.pathname === '/api/transactions/export';
  await page.route(exports, (route) => route.abort('failed'));
  await page.getByRole('button', { name: 'Exportar CSV' }).click();
  await expect(alert).toHaveText(network);
  await page.unroute(exports);

  await page.getByRole('button', { name: 'Novo lançamento' }).click();
  await page.getByLabel('Conta', { exact: true }).selectOption({ label: 'Nubank' });
  await page.getByLabel('Categoria', { exact: true }).selectOption({ label: 'Lazer' });
  await page.getByLabel('Valor').fill('15');
  await page.getByLabel('Data').fill('2026-09-10');
  await page.getByLabel('Descrição').fill('Cinema');
  const creates = (url: URL) => url.pathname === '/api/transactions';
  const answered = async (answer: (route: Route) => Promise<void>, sentence: string) => {
    await page.route(creates, (route) => (route.request().method() === 'POST' ? answer(route) : route.fallback()));
    await page.getByRole('button', { name: 'Criar lançamento' }).click();
    await expect(alert).toHaveText(sentence);
    await page.unroute(creates);
  };
  await answered((route) => route.fulfill({ status: 500 }), 'O servidor não conseguiu concluir a operação. Tente de novo em instantes.');
  await answered((route) => route.fulfill({ status: 403 }), 'Não foi possível concluir a operação (erro 403).');
  await answered((route) => route.abort('failed'), network);
});

/** 028: Editar on a row while a new transaction is still being saved. */
test('Editar while a new transaction is saving opens that row, and the save leaves it open', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-transactions-late'), 'Katherine Johnson');
  await createAccount(page, 'Nubank');
  await createTransaction(page, { account: 'Nubank', category: 'Alimentação', amount: '42', date: '2026-09-10', description: 'Mercado' });
  await showTransactionsBetween(page, '2026-09-01', '2026-09-30');

  const release = await holdBack(page, 'POST', '/api/transactions');
  await page.getByRole('button', { name: 'Novo lançamento' }).click();
  await page.getByLabel('Conta', { exact: true }).selectOption({ label: 'Nubank' });
  await page.getByLabel('Categoria', { exact: true }).selectOption({ label: 'Lazer' });
  await page.getByLabel('Valor').fill('30');
  await page.getByLabel('Data').fill('2026-09-12');
  await page.getByLabel('Descrição').fill('Cinema');
  await page.getByRole('button', { name: 'Criar lançamento' }).click();

  // The row's own values, not the ones typed for the new transaction.
  await page.getByRole('row', { name: /Mercado/ }).getByRole('button', { name: 'Editar' }).click();
  const description = page.getByLabel('Descrição');
  await expect(description).toHaveValue('Mercado');
  await expect(page.getByLabel('Valor')).toHaveValue('42.00');
  release();

  // Cinema lands; Mercado's form is still open, and saves Mercado.
  await expect(page.getByRole('row', { name: /Cinema/ })).toBeVisible();
  await description.fill('Mercado do bairro');
  await page.getByRole('button', { name: 'Salvar lançamento' }).click();
  await expect(page.getByRole('row', { name: /Mercado do bairro/ })).toBeVisible();
  await expect(page.getByRole('row', { name: /Cinema/ })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Salvar lançamento' })).toBeHidden();
});

/**
 * 024: more rows than a page, made by importing 120 rows rather than typing them. On the
 * way, the review pages its rows 100 at a time (IMP-15).
 */
test('a list longer than a page pages 50 at a time', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-pages'), 'Katherine Johnson');
  await createAccount(page, 'Nubank');

  const lines = Array.from({ length: 120 }, (_, index) => {
    const number = String(index + 1).padStart(3, '0');
    const day = String((index % 28) + 1).padStart(2, '0');
    return `${day}/08/2026;-${index + 1},00;Linha ${number}`;
  });
  await uploadStatement(page, 'Nubank', {
    name: 'cento-e-vinte.csv',
    mimeType: 'text/csv',
    buffer: Buffer.from(['Data;Valor;Descrição', ...lines].join('\n'), 'utf8'),
  });
  await expect(page.getByRole('heading', { name: '2. Mapeamento' })).toBeVisible();
  await page.getByLabel('Coluna de data').selectOption('Data');
  await page.getByLabel('Coluna de valor').selectOption('Valor');
  await page.getByLabel('Descrição', { exact: true }).check();
  await page.getByRole('button', { name: 'Continuar' }).click();

  const step = importStep(page);
  await expect(step.getByTestId('preview-counts')).toHaveText(/120 prontas · 0 duplicadas · 0 inválidas · 120 a importar/);
  await expect(step.getByText('Página 1 de 2 · 120 linhas')).toBeVisible();
  await expect(step.getByTestId('staged-row-Ready')).toHaveCount(100);
  await step.getByRole('button', { name: 'Próxima' }).click();
  await expect(step.getByText('Página 2 de 2 · 120 linhas')).toBeVisible();
  await expect(step.getByTestId('staged-row-Ready')).toHaveCount(20);
  await expect(step.getByRole('button', { name: 'Próxima' })).toBeDisabled();

  await commitImport(page);
  await expect(page.getByTestId('commit-summary')).toHaveText('120 lançamentos importados');
  await page.getByRole('link', { name: 'Ver lançamentos' }).click();

  const range = page.getByText(/^\d+–\d+ de 120$/);
  const listed = page.getByRole('row').filter({ hasText: /Linha \d{3}/ });
  await expect(range).toHaveText('1–50 de 120');
  await expect(listed).toHaveCount(50);
  await expect(page.getByRole('button', { name: 'Anterior' })).toBeDisabled();

  await page.getByRole('button', { name: 'Próxima' }).click();
  await expect(range).toHaveText('51–100 de 120');
  await page.getByRole('button', { name: 'Próxima' }).click();
  await expect(range).toHaveText('101–120 de 120');
  await expect(listed).toHaveCount(20);
  await expect(page.getByRole('button', { name: 'Próxima' })).toBeDisabled();
  await page.getByRole('button', { name: 'Anterior' }).click();
  await expect(range).toHaveText('51–100 de 120');

  // A new filter starts again on its first page.
  await page.getByLabel('Filtrar por categoria').selectOption({ label: 'Outros' });
  await expect(range).toHaveText('1–50 de 120');
});
