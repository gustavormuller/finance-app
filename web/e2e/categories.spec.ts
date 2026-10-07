import { expect, test, type Page } from '@playwright/test';

import { createAccount, createTransaction, devLogin, uniqueEmail, utcDaysAgo } from './support';

/**
 * Spec 024: `/categories`, against the real API and a real PostgreSQL. Every test signs in
 * as its own user, so it starts from the nine default categories and nothing else.
 */

/** The table of one kind: Receitas, Despesas or Transferências. */
function kind(page: Page, name: 'Receitas' | 'Despesas' | 'Transferências') {
  return page.getByRole('region', { name, exact: true });
}

/** The category's own row, by its name cell. */
function row(page: Page, name: string) {
  return page.getByRole('row').filter({ has: page.getByTestId('category-name').getByText(name, { exact: true }) });
}

function names(page: Page) {
  return page.getByTestId('category-name');
}

test('the default categories are grouped by kind, filtered by kind and found without accents', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-categories'), 'Ada Lovelace');
  await page.goto('/categories');

  await expect(kind(page, 'Receitas').getByTestId('category-name')).toHaveText(['Outras receitas', 'Salário']);
  await expect(kind(page, 'Despesas').getByTestId('category-name')).toHaveText([
    'Alimentação',
    'Lazer',
    'Moradia',
    'Outros',
    'Saúde',
    'Transporte',
  ]);
  await expect(kind(page, 'Transferências').getByTestId('category-name')).toHaveText(['Transferência']);
  await expect(page.getByRole('switch', { name: 'Só as sem uso (9)' })).toBeVisible();

  const filters = page.getByRole('group', { name: 'Filtrar por tipo' });
  await filters.getByRole('button', { name: 'Receitas' }).click();
  await expect(names(page)).toHaveText(['Outras receitas', 'Salário']);
  await expect(kind(page, 'Despesas')).toHaveCount(0);
  await filters.getByRole('button', { name: 'Todas' }).click();
  await expect(names(page)).toHaveCount(9);

  const search = page.getByLabel('Buscar categoria');
  await search.fill('saude');
  await expect(names(page)).toHaveText(['Saúde']);
  await search.fill('SALARIO');
  await expect(names(page)).toHaveText(['Salário']);
  await search.fill('nada parecido');
  await expect(names(page)).toHaveCount(0);
});

test('a category and a subcategory are created, renamed and deleted', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-categories-tree'), 'Grace Hopper');
  await page.goto('/categories');

  await page.getByRole('button', { name: 'Nova categoria' }).click();
  await page.getByLabel('Nome').fill('Educação');
  // The kinds are wire members, read in Portuguese.
  await expect(page.getByLabel('Tipo', { exact: true }).locator('option')).toHaveText(['Receita', 'Despesa', 'Transferência']);
  await expect(page.getByLabel('Tipo', { exact: true })).toHaveValue('Expense');
  await page.getByRole('button', { name: 'Criar categoria' }).click();
  await expect(row(page, 'Educação')).toBeVisible();
  await expect(kind(page, 'Despesas').getByTestId('category-name')).toContainText(['Educação']);

  // Under a main category, the subcategory takes its kind: the field is fixed.
  await page.getByRole('button', { name: 'Nova subcategoria em Educação' }).click();
  await expect(page.getByLabel('Fica dentro de')).toHaveValue(/.+/);
  await expect(page.getByLabel('Tipo', { exact: true })).toBeDisabled();
  await page.getByLabel('Nome').fill('Cursos');
  await page.getByRole('button', { name: 'Criar categoria' }).click();

  const parent = page.getByRole('button', { name: /^Educação\s*1$/ });
  await expect(parent).toHaveAttribute('aria-expanded', 'true');
  await expect(row(page, 'Cursos')).toBeVisible();
  await parent.click();
  await expect(row(page, 'Cursos')).toHaveCount(0);
  await parent.click();

  // The edit form opens right under the row it edits, on its values, and is the only form.
  await page.getByRole('button', { name: 'Editar Cursos' }).click();
  const formRow = row(page, 'Cursos').locator('xpath=following-sibling::tr[1]');
  await expect(formRow.getByLabel('Nome')).toHaveValue('Cursos');
  await expect(page.getByLabel('Nome')).toHaveCount(1);
  await formRow.getByLabel('Nome').fill('Cursos online');
  await page.getByRole('button', { name: 'Salvar', exact: true }).click();
  await expect(row(page, 'Cursos online')).toBeVisible();

  // A search that matches a subcategory keeps its main category beside it.
  await page.getByLabel('Buscar categoria').fill('online');
  await expect(names(page)).toHaveText(['Educação', 'Cursos online']);
  await page.getByLabel('Buscar categoria').fill('');

  // The transaction form offers it with its main category's name in front.
  await page.goto('/transactions');
  await page.getByRole('button', { name: 'Novo lançamento' }).click();
  await expect(page.getByLabel('Categoria', { exact: true }).locator('option', { hasText: 'Educação / Cursos online' })).toHaveCount(1);

  await page.goto('/categories');
  await page.getByRole('button', { name: 'Excluir Educação' }).click();
  await expect(page.getByRole('alert')).toHaveText("'Educação' ainda tem 1 subcategoria(s). Exclua-as antes.");

  await page.getByRole('button', { name: /^Educação\s*1$/ }).click();
  await page.getByRole('button', { name: 'Excluir Cursos online' }).click();
  await expect(row(page, 'Cursos online')).toHaveCount(0);
  await page.getByRole('button', { name: 'Excluir Educação' }).click();
  await expect(row(page, 'Educação')).toHaveCount(0);
  await expect(names(page)).toHaveCount(9);
});

test('a name in use and a category with transactions are refused with the reason', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-categories-refused'), 'Alan Turing');
  await createAccount(page, 'Nubank');
  await createTransaction(page, { account: 'Nubank', category: 'Alimentação', amount: '10', date: '2026-09-10', description: 'Feira' });
  await page.goto('/categories');

  await page.getByRole('button', { name: 'Nova categoria' }).click();
  await page.getByLabel('Nome').fill('Lazer');
  await page.getByRole('button', { name: 'Criar categoria' }).click();
  await expect(page.getByRole('alert')).toHaveText("Já existe uma categoria chamada 'Lazer' neste nível.");
  await page.getByRole('button', { name: 'Cancelar' }).click();

  await page.getByRole('button', { name: 'Excluir Alimentação' }).click();
  await expect(page.getByRole('alert')).toHaveText("'Alimentação' ainda tem 1 lançamento(s). Recategorize-os ou exclua-os antes.");
  await expect(row(page, 'Alimentação')).toBeVisible();

  // A kind left with no category says so instead of showing an empty table, and a new
  // category of that kind fills it.
  await page.getByRole('button', { name: 'Excluir Transferência' }).click();
  await expect(kind(page, 'Transferências')).toContainText('Nenhuma categoria deste tipo.');
  await page.getByRole('button', { name: 'Nova categoria' }).click();
  await page.getByLabel('Nome').fill('Entre contas');
  await page.getByLabel('Tipo', { exact: true }).selectOption({ label: 'Transferência' });
  await page.getByRole('button', { name: 'Criar categoria' }).click();
  await expect(kind(page, 'Transferências').getByTestId('category-name')).toHaveText(['Entre contas']);
});

/**
 * Use over the last 12 months, which end today on the API's (UTC) calendar: the
 * transactions are dated 20 and 40 days back, inside that window whatever today is.
 */
test("each category's use over the last 12 months, its share, and the unused ones", async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-categories-use'), 'Katherine Johnson');
  await createAccount(page, 'Nubank');
  await createTransaction(page, { account: 'Nubank', category: 'Alimentação', amount: '100', date: utcDaysAgo(20), description: 'Mercado' });
  await createTransaction(page, { account: 'Nubank', category: 'Alimentação', amount: '50', date: utcDaysAgo(40), description: 'Feira' });
  await createTransaction(page, { account: 'Nubank', category: 'Lazer', amount: '50', date: utcDaysAgo(20), description: 'Cinema' });
  await createTransaction(page, { account: 'Nubank', category: 'Salário', amount: '1000', date: utcDaysAgo(20), description: 'Salário' });

  await page.goto('/categories');

  const alimentacao = row(page, 'Alimentação');
  await expect(alimentacao.getByTestId('category-count')).toHaveText('2');
  await expect(alimentacao.getByTestId('amount')).toHaveText('−150,00');
  await expect(alimentacao.getByTestId('category-share')).toContainText('75,0%');
  await expect(row(page, 'Lazer').getByTestId('category-share')).toContainText('25,0%');
  await expect(row(page, 'Salário').getByTestId('amount')).toHaveText('+1.000,00');
  await expect(row(page, 'Salário').getByTestId('category-share')).toContainText('100,0%');
  await expect(row(page, 'Transferência').getByTestId('category-share')).toHaveCount(0);

  // The largest use first.
  await expect(kind(page, 'Despesas').getByTestId('category-name').first()).toHaveText('Alimentação');

  // A subcategory's use adds into its main category.
  await page.getByRole('button', { name: 'Nova subcategoria em Alimentação' }).click();
  await page.getByLabel('Nome').fill('Padaria');
  await page.getByRole('button', { name: 'Criar categoria' }).click();
  await createTransaction(page, { account: 'Nubank', category: 'Alimentação / Padaria', amount: '25', date: utcDaysAgo(20), description: 'Pão' });
  await page.goto('/categories');
  await expect(alimentacao.getByTestId('category-count')).toHaveText('3');
  await expect(alimentacao.getByTestId('amount')).toHaveText('−175,00');

  // Nine defaults plus Padaria, four of them used.
  const unused = page.getByRole('switch', { name: 'Só as sem uso (6)' });
  await unused.click();
  await expect(unused).toHaveAttribute('aria-checked', 'true');
  await expect(names(page)).toHaveText(['Outras receitas', 'Moradia', 'Outros', 'Saúde', 'Transporte', 'Transferência']);
});

test('a refusal the API sends as a 400 is explained in Portuguese', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-categories-400'), 'Ada Lovelace');
  await page.goto('/categories');

  // Spaces pass the field's `required`, and the API refuses the blank name.
  await page.getByRole('button', { name: 'Nova categoria' }).click();
  await page.getByLabel('Nome').fill('   ');
  await page.getByRole('button', { name: 'Criar categoria' }).click();
  await expect(page.getByRole('alert')).toHaveText('O nome é obrigatório.');
  await page.getByRole('button', { name: 'Cancelar' }).click();

  // A main category with a subcategory cannot become one.
  await page.getByRole('button', { name: 'Nova subcategoria em Lazer' }).click();
  await page.getByLabel('Nome').fill('Cinema');
  await page.getByRole('button', { name: 'Criar categoria' }).click();
  // Saved, as a person sees it, before Lazer is edited: a save that lands later closes
  // whatever form is open by then (spec 027, found on the way).
  await expect(page.getByRole('button', { name: 'Criar categoria' })).toBeHidden();
  await expect(page.getByRole('cell', { name: 'Cinema', exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Editar Lazer' }).click();
  await page.getByLabel('Fica dentro de').selectOption({ label: 'Alimentação (Despesa)' });
  await page.getByRole('button', { name: 'Salvar', exact: true }).click();
  await expect(page.getByRole('alert')).toHaveText('Essa categoria tem subcategorias, então não pode virar subcategoria.');
});
