import { fileURLToPath } from 'node:url';

import { expect, test, type Page } from '@playwright/test';

import { createAccount, devLogin, openImportTab, uniqueEmail } from './support';

/**
 * Spec E2E tests 63 to 66, against the real API and a real PostgreSQL, with the
 * fixture files in ./fixtures. Since 015 every import starts from the account's own
 * tab, where choosing a file starts it.
 */

const OFX = fileURLToPath(new URL('./fixtures/extrato.ofx', import.meta.url));
const CSV = fileURLToPath(new URL('./fixtures/nubank.csv', import.meta.url));
const XLSX = fileURLToPath(new URL('./fixtures/bb-extrato.xlsx', import.meta.url));

/** Step 1 through to the review, for an OFX. */
async function uploadOfx(page: Page, account: string) {
  await openImportTab(page, account);
  await page.getByLabel('Escolher arquivo').setInputFiles(OFX);

  await expect(page.getByRole('heading', { name: '3. Revisão' })).toBeVisible();
}

/** The current step's own controls; the history underneath offers the same verbs for other batches. */
function step(page: Page) {
  return page.getByTestId('import-step');
}

async function commit(page: Page) {
  await step(page).getByRole('button', { name: 'Confirmar importação' }).click();

  await expect(page.getByRole('heading', { name: '4. Concluído' })).toBeVisible();
}

/** Spec E2E test 63. */
test('an OFX is uploaded, reviewed, committed and its rows appear in the list', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-import-ofx'), 'Ada Lovelace');
  await createAccount(page, 'Nubank');

  await uploadOfx(page, 'Nubank');

  await expect(page.getByTestId('preview-counts')).toHaveText(/3 prontas · 0 duplicadas · 0 inválidas · 3 a importar/);
  await expect(page.getByRole('row', { name: /NETFLIX\.COM/ })).toBeVisible();

  await commit(page);
  await expect(page.getByTestId('commit-summary')).toHaveText('3 lançamentos importados');

  await page.getByRole('link', { name: 'Ver lançamentos' }).click();

  await expect(page.getByRole('row', { name: /Pagamento efetuado — NETFLIX\.COM/ })).toBeVisible();
  await expect(page.getByRole('row', { name: /EMPRESA LTDA/ }).getByTestId('amount')).toHaveText('+3.000,00');
  await expect(page.getByRole('row', { name: /PAG\*IFOOD/ }).getByTestId('amount')).toHaveText('−1.234,56');
});

/** Spec E2E test 64. The same file again writes nothing: every row is a duplicate. */
test('uploading the same OFX again marks every row duplicate and commits nothing', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-import-dup'), 'Grace Hopper');
  await createAccount(page, 'Inter');

  await uploadOfx(page, 'Inter');
  await commit(page);

  await uploadOfx(page, 'Inter');

  await expect(page.getByTestId('preview-counts')).toHaveText(/0 prontas · 3 duplicadas · 0 inválidas · 0 a importar/);
  await expect(page.getByTestId('staged-row-Duplicate')).toHaveCount(3);
  await expect(step(page).getByRole('button', { name: 'Confirmar importação' })).toBeDisabled();

  await step(page).getByRole('button', { name: 'Descartar' }).click();
  await expect(page.getByRole('heading', { name: '1. Arquivo' })).toBeVisible();

  await page.goto('/transactions');
  await expect(page.getByRole('row', { name: /NETFLIX\.COM/ })).toHaveCount(1);
});

/** Spec E2E test 65. */
test('a CSV is mapped with a live preview, reviewed and committed', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-import-csv'), 'Alan Turing');
  await createAccount(page, 'Nubank');

  await openImportTab(page, 'Nubank');
  await page.getByLabel('Escolher arquivo').setInputFiles(CSV);

  await expect(page.getByRole('heading', { name: '2. Mapeamento' })).toBeVisible();
  await expect(page.getByLabel('Delimitador')).toHaveValue(',');

  await page.getByLabel('Formato dos números').selectOption('en-US');
  await page.getByLabel('Coluna de data').selectOption('Data');
  await page.getByLabel('Coluna de valor').selectOption('Valor');
  await page.getByLabel('Descrição', { exact: true }).check();

  // The live preview reads 24/08 as 24 August under the default dd/MM/yyyy.
  const preview = page.getByTestId('mapping-preview');
  await expect(preview.getByText('24 ago 2026')).toBeVisible();
  await expect(preview.getByText('−58,00')).toBeVisible();

  await page.getByRole('button', { name: 'Continuar' }).click();

  await expect(page.getByRole('heading', { name: '3. Revisão' })).toBeVisible();
  await expect(page.getByTestId('preview-counts')).toHaveText(/3 prontas/);

  await commit(page);
  await expect(page.getByTestId('commit-summary')).toHaveText('3 lançamentos importados');

  await page.getByRole('link', { name: 'Ver lançamentos' }).click();
  await expect(page.getByRole('row', { name: /Padaria do bairro/ }).getByTestId('amount')).toHaveText('−12,50');
});

/** Spec E2E test 66. */
test('undoing a committed batch removes its rows from the list', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-import-undo'), 'Katherine Johnson');
  await createAccount(page, 'Caixa');

  await uploadOfx(page, 'Caixa');
  await commit(page);

  await step(page).getByRole('button', { name: 'Desfazer' }).click();
  await expect(step(page).getByRole('alertdialog')).toHaveText(/excluir 3 lançamentos\?/);
  await step(page).getByRole('button', { name: 'Confirmar' }).click();

  await expect(page.getByRole('heading', { name: '1. Arquivo' })).toBeVisible();
  await expect(page.getByText('Nenhuma importação nesta conta ainda.')).toBeVisible();

  await page.goto('/transactions');
  await expect(page.getByText(/nenhum lançamento/i)).toBeVisible();
  await expect(page.getByRole('row', { name: /NETFLIX\.COM/ })).toHaveCount(0);
});

/**
 * Spec 011 E2E test 18: typed date and number cells, two lines above the table and
 * two balance lines without a value, through the same mapping as a CSV.
 */
test('an .xlsx is mapped, reviewed and committed', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-import-xlsx'), 'Grace Hopper');
  await createAccount(page, 'Banco do Brasil');

  await openImportTab(page, 'Banco do Brasil');
  await page.getByLabel('Escolher arquivo').setInputFiles(XLSX);

  await expect(page.getByRole('heading', { name: '2. Mapeamento' })).toBeVisible();
  await expect(page.getByLabel('Delimitador')).toHaveCount(0);

  await page.getByLabel('Coluna de data').selectOption('Data');
  await page.getByLabel('Coluna de valor').selectOption('Valor (R$)');
  await page.getByLabel('Lançamento', { exact: true }).check();
  await page.getByLabel('Detalhes', { exact: true }).check();

  const preview = page.getByTestId('mapping-preview');
  await expect(preview.getByText('3 ago 2026')).toBeVisible();
  await expect(preview.getByText('−187,43')).toBeVisible();

  await page.getByRole('button', { name: 'Continuar' }).click();

  await expect(page.getByRole('heading', { name: '3. Revisão' })).toBeVisible();
  await expect(page.getByTestId('preview-counts')).toHaveText(/8 prontas/);

  await commit(page);
  await expect(page.getByTestId('commit-summary')).toHaveText('8 lançamentos importados');

  await page.getByRole('link', { name: 'Ver lançamentos' }).click();
  await expect(page.getByRole('row', { name: /SUPERMERCADO ZONA SUL/ }).getByTestId('amount')).toHaveText('−187,43');
  await expect(page.getByRole('row', { name: /Rende Fácil/ }).getByTestId('amount')).toHaveText('+0,30');
});

/**
 * Spec 015 E2E test 20: the account's card reflects the import, and a statement in
 * review is found from another account's tab and from the old /import address.
 */
test('the account shows the import in its card, and a review in progress is found from anywhere', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-import-account'), 'Ada Lovelace');
  await createAccount(page, 'Nubank', '1.000,00');
  await createAccount(page, 'Inter');

  await uploadOfx(page, 'Nubank');

  // Another account's tab says where the review is, instead of offering a drop zone.
  await openImportTab(page, 'Inter');
  await expect(page.getByText(/Há um extrato em revisão na conta Nubank\./)).toBeVisible();
  await expect(page.getByTestId('drop-zone')).toHaveCount(0);
  await page.getByRole('link', { name: 'Abrir a importação da conta Nubank' }).click();
  await expect(page.getByRole('heading', { name: 'Nubank', exact: true })).toBeVisible();
  await expect(page.getByText('O extrato extrato.ofx ainda está em revisão.')).toBeVisible();

  // So does the address the import used to have.
  await page.goto('/import');
  await expect(page).toHaveURL(/\/accounts\/[^/?]+\?tab=import$/);
  await expect(page.getByRole('heading', { name: 'Nubank', exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Continuar a revisão' }).click();
  await expect(page.getByTestId('preview-counts')).toHaveText(/3 prontas/);

  await commit(page);

  // 1.000,00 opening, then +3.000,00 −1.234,56 −55,90.
  const card = page.getByRole('listitem').filter({ has: page.getByRole('link', { name: 'Nubank', exact: true }) });
  await expect(card.getByTestId('amount')).toHaveText('+2.709,54');
  await expect(card).toContainText(/Último extrato em \d{2}\/\d{2}/);
  await expect(page.getByTestId('import-history-row')).toHaveCount(1);
});
