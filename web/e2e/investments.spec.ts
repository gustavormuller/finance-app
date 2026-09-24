import { expect, test, type Page } from '@playwright/test';

import { devLogin, syncMarketData, uniqueEmail } from './support';

/**
 * Spec 007 E2E (tests 31–33), against the real API process and a real PostgreSQL.
 *
 * The API runs with `MarketData:FakeProviders=true` (scripts/verify-e2e.sh): the latest
 * close is always 10, dated yesterday (UTC), and a buy dated today is valued at it. PETR4
 * is BRL, so no FX is involved (the fakes price USDBRL at 0.05). The catalogue is shared
 * and kept between runs: the first run registers PETR4, later runs reuse that entry. Each test brings its own user.
 *
 * Every test here triggers a manual sync, and the sync gate refuses a second run while
 * one is going (429). So:
 * - this file runs in its own Playwright project, after `market-data.spec.ts` has
 *   finished (playwright.config.ts), and never overlaps test 26, which asserts a 202;
 * - its tests run one after another (`mode: 'default'`), never overlapping each other;
 * - a 429 here is waited out, because the rebuild after the previous run's sync can still
 *   hold the gate for a moment after that run reads `Succeeded`.
 */
test.describe.configure({ mode: 'default' });

/** Registers PETR4 (or reuses the shared catalogue entry) as the user's asset, and opens it. */
async function addPetr4(page: Page) {
  await page.goto('/investments');
  await page.getByRole('button', { name: 'Cadastrar novo ativo' }).click();

  const form = page.getByRole('form', { name: 'Cadastrar e adicionar ativo' });
  await form.getByLabel('Ticker').fill('PETR4');
  // Exact: "Símbolo no provedor" contains the word too. Class and currency default to
  // StockBr and BRL.
  await form.getByLabel('Provedor', { exact: true }).selectOption('Brapi');
  await form.getByLabel('Símbolo no provedor').fill('PETR4');
  await form.getByRole('button', { name: 'Cadastrar e adicionar' }).click();

  await expect(page.getByRole('heading', { name: 'PETR4' })).toBeVisible();
  return page.url().split('/').pop()!;
}

/** A movement through the real form, dated today (its default). */
async function recordMovement(page: Page, values: { kind: string; quantity?: string; unitPrice?: string; amount?: string; fees?: string }) {
  await page.getByRole('button', { name: 'Nova movimentação' }).click();
  const form = page.getByRole('form', { name: 'Movimentação' });
  await form.getByLabel('Tipo').selectOption(values.kind);
  if (values.quantity !== undefined) await form.getByLabel('Quantidade').fill(values.quantity);
  if (values.unitPrice !== undefined) await form.getByLabel('Preço unitário').fill(values.unitPrice);
  if (values.amount !== undefined) await form.getByLabel('Valor recebido').fill(values.amount);
  if (values.fees !== undefined) await form.getByLabel('Taxas').fill(values.fees);
  await form.getByRole('button', { name: 'Registrar movimentação' }).click();
  // The form closes only once the POST, and the rebuild inside it, have succeeded.
  await expect(form).toBeHidden();
}

/** The one figure under `label` in the asset's summary. */
function summaryFigure(page: Page, label: string) {
  return page.getByTestId('asset-summary').locator('div').filter({ has: page.getByText(label, { exact: true }) }).locator('dd');
}

/** Tests 31–33 all start here: PETR4 held, priced, and bought 100 @ 9,50 with 4,90 in fees. */
async function holdHundredPetr4(page: Page, prefix: string) {
  await devLogin(page, uniqueEmail(prefix), 'Ada Lovelace');
  const assetId = await addPetr4(page);
  await syncMarketData(page);

  await page.getByRole('button', { name: 'Nova movimentação' }).click();
  const form = page.getByRole('form', { name: 'Movimentação' });
  await form.getByLabel('Quantidade').fill('100');
  await form.getByLabel('Preço unitário').fill('9,50');
  await form.getByLabel('Taxas').fill('4,90');
  await expect(form.getByTestId('movement-total')).toContainText('R$ 954,90');
  await form.getByRole('button', { name: 'Registrar movimentação' }).click();
  await expect(form).toBeHidden();

  return assetId;
}

/** Spec E2E test 31. */
test('PETR4 is added, bought 100, and its position shows with a value', async ({ page }) => {
  const assetId = await holdHundredPetr4(page, 'e2e-invest-buy');

  // On the detail page: 100 at the fake close of 10, cost 954,90.
  await expect(summaryFigure(page, 'Quantidade')).toHaveText('100');
  await expect(summaryFigure(page, 'Valor')).toHaveText('R$ 1.000,00');
  await expect(summaryFigure(page, 'Resultado')).toContainText('+R$ 45,10');

  // And on the positions list, with the total row.
  await page.getByRole('link', { name: '← Investimentos' }).click();
  const row = page.getByTestId(`position-${assetId}`);
  await expect(row).toContainText('PETR4');
  await expect(row).toContainText('100');
  await expect(row).toContainText('R$ 1.000,00');
  await expect(page.getByTestId('positions-total')).toContainText('R$ 1.000,00');
});

/** Spec E2E test 32. */
test('a dividend raises Proventos and leaves the quantity alone', async ({ page }) => {
  await holdHundredPetr4(page, 'e2e-invest-dividend');
  await expect(summaryFigure(page, 'Proventos')).toHaveText('R$ 0,00');

  await recordMovement(page, { kind: 'Dividend', amount: '12,34' });

  await expect(summaryFigure(page, 'Proventos')).toHaveText('R$ 12,34');
  await expect(summaryFigure(page, 'Quantidade')).toHaveText('100');
  await expect(summaryFigure(page, 'Valor')).toHaveText('R$ 1.000,00');
});

/**
 * Spec E2E test 33. A dividend is left behind on purpose: the buy alone is deleted, the
 * position drops to zero with income still on it, and the row is hidden, not removed.
 */
test('deleting the buy makes the position disappear from the list', async ({ page }) => {
  const assetId = await holdHundredPetr4(page, 'e2e-invest-delete');
  await recordMovement(page, { kind: 'Dividend', amount: '5' });

  const buy = page.locator('[data-testid^="movement-"]').filter({ hasText: 'Compra' });
  await buy.getByRole('button', { name: 'Excluir' }).click();
  await expect(buy).toHaveCount(0);
  await expect(summaryFigure(page, 'Quantidade')).toHaveText('0');

  await page.getByRole('link', { name: '← Investimentos' }).click();
  await expect(page.getByText('Nenhuma posição em aberto.')).toBeVisible();
  await expect(page.getByTestId(`position-${assetId}`)).toHaveCount(0);

  // Hidden, not gone: the asset is still held, at zero.
  await page.getByLabel('Mostrar ativos sem posição (1)').check();
  await expect(page.getByTestId(`position-${assetId}`)).toContainText('PETR4');
});
