import { fileURLToPath } from 'node:url';

import { expect, test, type Page } from '@playwright/test';

import { createAccount, devLogin, uniqueEmail } from './support';

/**
 * Spec 009 E2E tests 28 and 29, against the real API and a real PostgreSQL, with the API on
 * `Ai:FakeProvider` (scripts/verify-e2e.sh): no request leaves the machine.
 *
 * Every test signs in as a brand-new user, so AI starts off, nothing is shared between tests
 * or runs, and there is no state to put back. Neither test syncs market data, so both run in
 * the `chromium` project.
 */

const OFX = fileURLToPath(new URL('./fixtures/extrato.ofx', import.meta.url));

/** The month `extrato.ofx`'s rows fall in, as the month selector shows it. */
const FIXTURE_MONTH = { key: '2026-09', label: 'setembro de 2026' };

/** New users have AI off; this turns it on through the real switch. */
async function enableAi(page: Page) {
  await page.goto('/');
  await page.getByRole('link', { name: 'Configurações' }).click();

  await expect(page.getByTestId('ai-state')).toHaveText('Desligada');
  await page.getByRole('switch', { name: 'Usar IA nesta conta' }).check();
  await expect(page.getByTestId('ai-state')).toHaveText('Ligada');
}

/** Step 1 through to the review, for `extrato.ofx`. */
async function uploadOfx(page: Page, account: string) {
  await page.goto('/import');
  await page.getByLabel('Conta').selectOption({ label: account });
  await page.getByLabel('Arquivo').setInputFiles(OFX);
  await page.getByRole('button', { name: 'Enviar' }).click();

  await expect(page.getByRole('heading', { name: '3. Revisão' })).toBeVisible();
}

function step(page: Page) {
  return page.getByTestId('import-step');
}

/**
 * Pages the dashboard back to the fixture's month, so the test holds on any date and not
 * only while that month is the current one. The month is in the past (never refused as a
 * future one) and fixed, so the fake's figures are too.
 */
async function showFixtureMonth(page: Page) {
  const selected = page.getByTestId('selected-month');
  await expect(selected).toBeVisible();

  const now = new Date();
  const current = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}`;
  expect(current >= FIXTURE_MONTH.key, 'the fixture month must not be in the future').toBe(true);

  const [year, month] = FIXTURE_MONTH.key.split('-').map(Number);
  const back = (now.getFullYear() - year) * 12 + (now.getMonth() + 1 - month);
  for (let i = 0; i < back; i++) {
    await page.getByRole('button', { name: 'Mês anterior' }).click();
  }

  await expect(selected).toHaveText(FIXTURE_MONTH.label);
}

/** Spec E2E test 28. */
test('with AI on, "Sugerir com IA" changes the preview rows in place (fake provider)', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-ai-suggest'), 'Ada Lovelace');
  await enableAi(page);
  await createAccount(page, 'Nubank');

  await uploadOfx(page, 'Nubank');

  // A new user has no history, so every row starts on its sign's default category.
  const firstRow = page.getByRole('combobox', { name: 'Categoria da linha 1' });
  await expect(firstRow.locator('option:checked')).toHaveText('Outros');
  await expect(page.getByTestId('ai-marker')).toHaveCount(0);

  await step(page).getByRole('button', { name: 'Sugerir com IA' }).click();

  await expect(step(page).getByRole('status')).toHaveText('3 categorias sugeridas pela IA.');
  await expect(page.getByTestId('ai-marker')).toHaveCount(3);
  // The fake answers each row's kind with its first category that is not a default.
  await expect(firstRow.locator('option:checked')).toHaveText('Alimentação');
  await expect(
    page.getByRole('row', { name: /EMPRESA LTDA/ }).getByRole('combobox').locator('option:checked'),
  ).toHaveText('Salário');

  // The call is on the account's bill.
  await page.getByRole('link', { name: 'Configurações' }).click();
  await expect(page.getByTestId('ai-spend')).toContainText('1 chamada');
});

/** Spec E2E test 29. */
test('"Gerar análise" on the dashboard fills the card with the month\'s analysis (fake provider)', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-ai-analysis'), 'Grace Hopper');
  await enableAi(page);
  await createAccount(page, 'Inter');

  await uploadOfx(page, 'Inter');
  await step(page).getByRole('button', { name: 'Confirmar importação' }).click();
  await expect(page.getByRole('heading', { name: '4. Concluído' })).toBeVisible();

  await page.goto('/');
  await showFixtureMonth(page);

  const card = page.getByTestId('analysis-card');
  await expect(card).toContainText('Nenhuma análise de setembro de 2026 ainda.');
  await card.getByRole('button', { name: 'Gerar análise' }).click();

  // The card polls every 3 s while the row is Pending or Running.
  const content = card.getByTestId('analysis-content');
  await expect(content).toBeVisible({ timeout: 15_000 });
  for (const heading of ['Resumo', 'Onde o dinheiro foi', 'O que mudou', 'Investimentos', 'Sugestões']) {
    await expect(content.getByRole('heading', { name: heading })).toBeVisible();
  }
  // The fake quotes the month and its expense from what was sent: 55,90 + 1.234,56.
  await expect(content).toContainText(`Análise de teste de ${FIXTURE_MONTH.key}`);
  await expect(content).toContainText('As saídas do mês somaram R$ 1290.46.');
  await expect(card.getByRole('button', { name: 'Regenerar' })).toBeEnabled();
});
