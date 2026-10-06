import { expect, test, type Page } from '@playwright/test';

import { devLogin, localToday, syncMarketData, uniqueEmail, uniqueTicker } from './support';

/**
 * Spec 007 E2E (tests 31–33) and 024's investments flows, against the real API process
 * and a real PostgreSQL.
 *
 * The API runs with `MarketData:FakeProviders=true` (scripts/verify-e2e.sh): the latest
 * close is always 10, dated yesterday (UTC), and a buy dated today is valued at it. PETR4
 * is BRL, so no FX is involved (the fakes price USDBRL at 0.05). The catalogue is shared
 * and kept between runs: the first run registers PETR4, later runs reuse that entry. Each test brings its own user.
 *
 * Most tests here trigger a manual sync, and the sync gate refuses a second run while
 * one is going (429); one asserts that a new ticker has no close until a sync. So:
 * - this file runs in its own Playwright project, after `market-data.spec.ts` has
 *   finished (playwright.config.ts), and never overlaps test 26, which asserts a 202;
 * - its tests run one after another (`mode: 'default'`), never overlapping each other;
 * - a 429 here is waited out, because the rebuild after the previous run's sync can still
 *   hold the gate for a moment after that run reads `Succeeded`.
 */
test.describe.configure({ mode: 'default' });

/** Registers a ticker on brapi (or reuses the shared catalogue entry) as the user's asset, opens it, and returns its id. */
async function addNewAsset(page: Page, ticker: string) {
  await page.goto('/investments');
  await page.getByRole('button', { name: 'Cadastrar novo ativo' }).click();

  const form = page.getByRole('form', { name: 'Cadastrar e adicionar ativo' });
  await form.getByLabel('Ticker').fill(ticker);
  // Exact: "Símbolo no provedor" contains the word too. Class and currency default to
  // StockBr and BRL.
  await form.getByLabel('Provedor', { exact: true }).selectOption('Brapi');
  await form.getByLabel('Símbolo no provedor').fill(ticker);
  await form.getByRole('button', { name: 'Cadastrar e adicionar' }).click();

  await expect(page.getByRole('heading', { name: ticker })).toBeVisible();
  return page.url().split('/').pop()!;
}

const addPetr4 = (page: Page) => addNewAsset(page, 'PETR4');

/** The API's message for one field of the movement form, shown under it. */
function underMovementField(page: Page, label: string) {
  return page
    .getByRole('form', { name: 'Movimentação' })
    .locator('div')
    .filter({ has: page.getByLabel(label, { exact: true }) })
    .getByRole('alert');
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

/**
 * Spec E2E test 31; 024: the value chart, "O que mais contribuiu", "Patrimônio investido"
 * and its allocation, and the dashboard's hero adding what is invested.
 */
test('PETR4 is added, bought 100, and its position shows with a value', async ({ page }) => {
  const assetId = await holdHundredPetr4(page, 'e2e-invest-buy');

  // On the detail page: 100 at the fake close of 10, cost 954,90.
  await expect(summaryFigure(page, 'Quantidade')).toHaveText('100');
  await expect(summaryFigure(page, 'Valor')).toHaveText('R$ 1.000,00');
  await expect(summaryFigure(page, 'Resultado')).toContainText('+R$ 45,10');
  await expect(page.getByTestId('value-chart')).toBeVisible();

  // And on the positions list, with the total row.
  await page.getByRole('link', { name: '← Investimentos' }).click();
  const row = page.getByTestId(`position-${assetId}`);
  await expect(row).toContainText('PETR4');
  await expect(row).toContainText('100');
  await expect(row).toContainText('R$ 1.000,00');
  await expect(page.getByTestId('positions-total')).toContainText('R$ 1.000,00');

  const contributor = page.getByTestId(`contributor-${assetId}`);
  await expect(contributor).toContainText('PETR4');
  await expect(contributor).toContainText('+R$ 45,10');
  await expect(page.getByTestId('contributors').getByRole('link', { name: 'Ver a posição' })).toBeVisible();
  await expect(page.getByTestId('holdings-total')).toHaveText('R$ 1.000,00');
  await expect(page.getByTestId('holdings-result')).toHaveText('+R$ 45,10 sobre o custo');
  await expect(page.getByTestId('allocation-StockBr')).toContainText(/Ação \(B3\)\s*100,0%/);

  // The dashboard's net worth is the accounts, none here, plus what is invested.
  await page.goto('/');
  await expect(page.getByTestId('net-worth-total').getByTestId('amount')).toHaveText('+1.000,00');
  await expect(page.getByTestId('total-balance').getByTestId('amount')).toHaveText('+0,00');
  await expect(page.getByTestId('hero-invested')).toHaveText(/investido R\$\s*\+1\.000,00/);
  await page.getByTestId('hero-invested').click();
  await expect(page).toHaveURL(/\/investments$/);
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
 * Spec 016 E2E test 15. The fakes store USDBRL at 0,05 on every sync, so the latest rate is
 * always 0,05 and R$ 1.000,00 is US$ 20.000,00. The choice is the browser's: a reload keeps it.
 */
test('US$ shows the position and the total at the latest dollar, and survives a reload', async ({ page }) => {
  const assetId = await holdHundredPetr4(page, 'e2e-invest-dollar');
  await page.getByRole('link', { name: '← Investimentos' }).click();
  const row = page.getByTestId(`position-${assetId}`);
  await expect(row).toContainText('R$ 1.000,00');

  const currency = page.getByRole('group', { name: 'Moeda' });
  await currency.getByRole('button', { name: 'US$' }).click();

  await expect(row).toContainText('US$ 20.000,00');
  await expect(page.getByTestId('positions-total')).toContainText('US$ 20.000,00');
  await expect(page.getByTestId('holdings-total')).toHaveText('US$ 20.000,00');
  await expect(page.getByTestId('currency-note')).toContainText('US$ 1 = R$ 0,05');

  await page.reload();
  await expect(currency.getByRole('button', { name: 'US$' })).toHaveAttribute('aria-pressed', 'true');
  await expect(page.getByTestId(`position-${assetId}`)).toContainText('US$ 20.000,00');

  await currency.getByRole('button', { name: 'R$' }).click();
  await expect(page.getByTestId(`position-${assetId}`)).toContainText('R$ 1.000,00');
  await expect(page.getByTestId('currency-note')).toHaveCount(0);
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

/**
 * 024: a sell keeps the average and realises a result, an edit replays the history, a JCP
 * is income, and a split adds units at no cost. Deleting the buy would leave the sell
 * selling what was never held, so it is refused.
 */
test('a sell, a JCP, a split and an edit move the position; a delete that uncovers a sell is refused', async ({ page }) => {
  await holdHundredPetr4(page, 'e2e-invest-sell');

  // 100 at an average of 9,549; selling 40 at 10 realises 400 − 381,96.
  await recordMovement(page, { kind: 'Sell', quantity: '40', unitPrice: '10' });
  await expect(summaryFigure(page, 'Quantidade')).toHaveText('60');
  await expect(summaryFigure(page, 'Resultado realizado')).toHaveText('+R$ 18,04');

  const sell = page.locator('[data-testid^="movement-"]').filter({ hasText: 'Venda' });
  await sell.getByRole('button', { name: 'Editar' }).click();
  const form = page.getByRole('form', { name: 'Movimentação' });
  await expect(form.getByLabel('Quantidade')).toHaveValue('40');
  await form.getByLabel('Quantidade').fill('50');
  await form.getByRole('button', { name: 'Salvar movimentação' }).click();
  await expect(form).toBeHidden();
  await expect(summaryFigure(page, 'Quantidade')).toHaveText('50');
  await expect(summaryFigure(page, 'Resultado realizado')).toHaveText('+R$ 22,55');

  await recordMovement(page, { kind: 'Jcp', amount: '3,21' });
  await expect(summaryFigure(page, 'Proventos')).toHaveText('R$ 3,21');

  // A split of 50 more units: 100 held at half the average, the same money.
  await recordMovement(page, { kind: 'Split', quantity: '50' });
  await expect(summaryFigure(page, 'Quantidade')).toHaveText('100');
  await expect(summaryFigure(page, 'Preço médio')).toHaveText('R$ 4,7745');
  await expect(summaryFigure(page, 'Valor')).toHaveText('R$ 1.000,00');

  const buy = page.locator('[data-testid^="movement-"]').filter({ hasText: 'Compra' });
  await buy.getByRole('button', { name: 'Excluir' }).click();
  await expect(page.getByRole('alert')).toHaveText('Quantidade vendida maior que a posição');
  await expect(buy).toHaveCount(1);
});

/** 024: what the movement rules refuse, each under the field it names, and Cancelar. */
test('a movement the rules refuse is explained under its field', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-invest-rules'), 'Grace Hopper');
  await addNewAsset(page, uniqueTicker('MOV'));

  await page.getByRole('button', { name: 'Nova movimentação' }).click();
  const form = page.getByRole('form', { name: 'Movimentação' });
  const future = new Date(Date.now() + 10 * 86_400_000).toISOString().slice(0, 10);

  // Every rule broken at once is reported at once.
  await form.getByLabel('Data').fill(future);
  await form.getByLabel('Quantidade').fill('0');
  await form.getByLabel('Preço unitário').fill('10');
  await form.getByLabel('Taxas').fill('-1');
  await form.getByRole('button', { name: 'Registrar movimentação' }).click();
  await expect(underMovementField(page, 'Quantidade')).toHaveText('Quantidade deve ser positiva');
  await expect(underMovementField(page, 'Taxas')).toHaveText('Taxas não podem ser negativas');
  await expect(underMovementField(page, 'Data')).toHaveText('Data fora do intervalo');

  // Nothing held, so nothing to sell.
  await form.getByLabel('Tipo').selectOption('Sell');
  await form.getByLabel('Data').fill(localToday());
  await form.getByLabel('Quantidade').fill('10');
  await form.getByLabel('Taxas').fill('0');
  await form.getByRole('button', { name: 'Registrar movimentação' }).click();
  await expect(underMovementField(page, 'Quantidade')).toHaveText('Quantidade vendida maior que a posição');

  await form.getByLabel('Tipo').selectOption('Dividend');
  await form.getByLabel('Valor recebido').fill('0');
  await form.getByRole('button', { name: 'Registrar movimentação' }).click();
  await expect(underMovementField(page, 'Valor recebido')).toHaveText('Valor deve ser positivo');

  await form.getByRole('button', { name: 'Cancelar' }).click();
  await expect(form).toBeHidden();
  await expect(page.getByText('Nenhuma movimentação registrada.')).toBeVisible();
});

/** 024: nothing held yet. */
test('an empty portfolio says so, with no returns to show', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-invest-empty'), 'Alan Turing');
  await page.goto('/investments');

  await expect(page.getByText('Nenhum ativo na carteira ainda.')).toBeVisible();
  await expect(page.getByTestId('returns-hero')).toHaveCount(0);
  await expect(page.getByRole('heading', { name: 'Adicionar ativo' })).toBeVisible();
});

/**
 * 024: a ticker someone else put in the shared catalogue is found and added, once; and
 * an asset is removed only once no movement is recorded on it (ADR-006).
 */
test('an asset found in the catalogue is added once, and removed once nothing is recorded on it', async ({ page }) => {
  const ticker = uniqueTicker('CAT');
  await devLogin(page, uniqueEmail('e2e-invest-registers'), 'Ada Lovelace');
  await addNewAsset(page, ticker);

  await devLogin(page, uniqueEmail('e2e-invest-finds'), 'Katherine Johnson');
  await page.goto('/investments');
  const search = page.getByRole('search').filter({ has: page.getByLabel('Buscar no catálogo') });
  await search.getByLabel('Buscar no catálogo').fill(`${ticker}X`);
  await search.getByRole('button', { name: 'Buscar' }).click();
  await expect(page.getByText('Nenhum ativo corresponde a esta busca. Cadastre-o abaixo.')).toBeVisible();

  await search.getByLabel('Buscar no catálogo').fill(ticker.toLowerCase());
  await search.getByRole('button', { name: 'Buscar' }).click();
  const result = page.locator('[data-testid^="catalogue-result-"]').filter({ hasText: ticker });
  await expect(result).toContainText('Ação (B3) · BRL');
  await result.getByRole('button', { name: 'Adicionar' }).click();
  await expect(page.getByRole('heading', { name: ticker })).toBeVisible();
  await expect(page.getByText('Nenhuma movimentação registrada.')).toBeVisible();
  const asset = page.url();

  await page.goto('/investments');
  await search.getByLabel('Buscar no catálogo').fill(ticker);
  await search.getByRole('button', { name: 'Buscar' }).click();
  await result.getByRole('button', { name: 'Adicionar' }).click();
  await expect(page.getByRole('alert')).toHaveText('Você já possui este ativo na carteira.');

  await page.goto(asset);
  await recordMovement(page, { kind: 'Buy', quantity: '1', unitPrice: '10' });
  await page.getByRole('button', { name: 'Remover ativo' }).click();
  await expect(page.getByRole('alert')).toHaveText('O ativo tem movimentações. Exclua-as antes de remover o ativo.');

  await page.locator('[data-testid^="movement-"]').filter({ hasText: 'Compra' }).getByRole('button', { name: 'Excluir' }).click();
  await expect(page.getByText('Nenhuma movimentação registrada.')).toBeVisible();
  await page.getByRole('button', { name: 'Remover ativo' }).click();
  await expect(page).toHaveURL(/\/investments$/);
  await expect(page.getByText('Nenhum ativo na carteira ainda.')).toBeVisible();
});

/**
 * 024: a ticker new to the catalogue has no close until a sync fetches its history. The
 * sync's snapshot rebuild runs after the run reads Succeeded, so the page is read again
 * until it has.
 */
test('a new ticker shows "Sem cotação" until a sync prices it', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-invest-unpriced'), 'Grace Hopper');
  const assetId = await addNewAsset(page, uniqueTicker('SEM'));

  await expect(summaryFigure(page, 'Cotação')).toHaveText('Sem cotação');
  await recordMovement(page, { kind: 'Buy', quantity: '10', unitPrice: '8' });
  await expect(summaryFigure(page, 'Quantidade')).toHaveText('10');
  await expect(summaryFigure(page, 'Valor')).toHaveText('—');

  await page.getByRole('link', { name: '← Investimentos' }).click();
  const row = page.getByTestId(`position-${assetId}`);
  await expect(row).toContainText('Sem cotação');

  await syncMarketData(page);
  await expect(async () => {
    await page.reload();
    await expect(row).toContainText('R$ 100,00', { timeout: 1_000 });
  }).toPass({ timeout: 15_000 });
  await expect(row).not.toContainText('Sem cotação');
});
