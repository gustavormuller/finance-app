import { expect, test, type Page } from '@playwright/test';

import { devLogin, syncMarketData, uniqueEmail } from './support';

/**
 * Spec 025 E2E (tests 23–26), against the real API process and a real PostgreSQL.
 *
 * The API runs with `MarketData:FakeProviders=true`. The Yahoo fake loads 1000 days of
 * history ending at a close of 12,50 on the sync's `to`; brapi's fake ends at 10,00. The
 * catalogue is shared and kept between runs, so every ticker here is new.
 *
 * Every test here syncs, so this file runs in its own Playwright project, after `returns`
 * (playwright.config.ts), and its tests run one after another.
 */
test.describe.configure({ mode: 'default' });

/** A ticker no earlier run registered. */
function uniqueTicker(prefix = 'YH') {
  return `${prefix}${crypto.randomUUID().replaceAll('-', '').slice(0, 8).toUpperCase()}`;
}

function registration(page: Page) {
  const form = page.getByRole('form', { name: 'Cadastrar ativo' });
  return {
    form,
    ticker: form.getByLabel('Ticker'),
    assetClass: form.getByLabel('Classe'),
    // Exact: "Símbolo no provedor" contains the word too.
    provider: form.getByLabel('Provedor', { exact: true }),
    symbol: form.getByLabel('Símbolo no provedor'),
    currency: form.getByLabel('Moeda'),
    submit: form.getByRole('button', { name: 'Cadastrar ativo' }),
  };
}

/** Spec E2E test 23. */
test('Yahoo is the default, its symbols and currencies are suggested, and a sync needs no key', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-yahoo'), 'Ada Lovelace');
  await page.goto('/market-data');
  const fields = registration(page);

  await expect(fields.provider).toHaveValue('Yahoo');
  await expect(fields.provider.locator('option').first()).toHaveText(
    'Yahoo Finance (B3, EUA, índices, cripto em US$, câmbio — sem chave)',
  );

  // The well-known names, suggested but not registered: the catalogue keeps them between runs.
  await fields.ticker.fill('IBOV');
  await fields.assetClass.selectOption('Index');
  await expect(fields.symbol).toHaveValue('^BVSP');
  await expect(fields.currency).toHaveValue('BRL');
  await fields.ticker.fill('USD');
  await fields.assetClass.selectOption('Currency');
  await expect(fields.symbol).toHaveValue('BRL=X');
  await expect(fields.currency).toHaveValue('BRL');

  const stock = uniqueTicker();
  await fields.ticker.fill(stock);
  await fields.assetClass.selectOption('StockBr');
  await expect(fields.symbol).toHaveValue(`${stock}.SA`);
  await expect(fields.currency).toHaveValue('BRL');
  await fields.submit.click();
  await expect(fields.ticker).toHaveValue('');

  const coin = uniqueTicker('YC');
  await fields.ticker.fill(coin);
  await fields.assetClass.selectOption('Crypto');
  await expect(fields.symbol).toHaveValue(`${coin}-USD`);
  await expect(fields.currency).toHaveValue('USD');
  await fields.submit.click();
  await expect(fields.ticker).toHaveValue('');

  const index = uniqueTicker('YI');
  await fields.ticker.fill(index);
  await fields.assetClass.selectOption('Index');
  await expect(fields.symbol).toHaveValue(`^${index}`);
  await fields.submit.click();
  await expect(fields.ticker).toHaveValue('');

  await page.getByLabel('Buscar ativo').fill(stock);
  await page.getByRole('button', { name: 'Buscar' }).click();
  const row = page.locator('[data-testid^="market-asset-"]');
  await expect(row).toHaveCount(1);
  await expect(row).toContainText('Yahoo Finance');
  await expect(row).toContainText(`${stock}.SA`);
  await expect(row).toContainText('Histórico completo na próxima sincronização.');

  const accepted = page.waitForResponse(
    (response) => response.url().endsWith('/api/market-data/sync') && response.request().method() === 'POST',
  );
  await page.getByRole('button', { name: 'Sincronizar agora' }).click();
  const { syncRunId } = (await (await accepted).json()) as { syncRunId: string };
  const run = page.getByTestId(`sync-run-${syncRunId}`);
  await expect(run.getByTestId('sync-run-status')).toHaveText('Concluída', { timeout: 30_000 });
  await expect(run.getByTestId('provider-summary-Yahoo')).toContainText('Yahoo Finance');
  await expect(run.getByTestId('provider-summary-Yahoo')).toContainText(/linhas? gravadas?/);
  await expect(run.getByText('com falha')).toHaveCount(0);

  // The catalogue reads itself again when the run ends.
  await expect(row).not.toContainText('Histórico completo na próxima sincronização.');
});

/** Spec E2E test 24. */
test('a Yahoo symbol quoted in another currency is refused under Moeda', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-yahoo-currency'), 'Ada Lovelace');
  await page.goto('/market-data');
  const fields = registration(page);

  const stock = uniqueTicker();
  await fields.ticker.fill(stock);
  await fields.currency.selectOption('USD');
  await fields.submit.click();

  await expect(fields.form.getByText(`O símbolo ${stock}.SA é cotado em BRL no Yahoo Finance.`)).toBeVisible();
  await expect(fields.ticker).toHaveValue(stock);
});

/** The one figure under `label` in the asset's summary. */
function summaryFigure(page: Page, label: string) {
  return page.getByTestId('asset-summary').locator('div').filter({ has: page.getByText(label, { exact: true }) }).locator('dd');
}

/** Spec E2E test 25: what the owner does to move an asset off brapi. */
test('an asset held on brapi is moved to Yahoo, and its prices and position follow', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-yahoo-move'), 'Ada Lovelace');
  const ticker = uniqueTicker('YM');

  await page.goto('/investments');
  await page.getByRole('button', { name: 'Cadastrar novo ativo' }).click();
  const asset = page.getByRole('form', { name: 'Cadastrar e adicionar ativo' });
  await asset.getByLabel('Ticker').fill(ticker);
  await asset.getByLabel('Provedor', { exact: true }).selectOption('Brapi');
  await expect(asset.getByLabel('Símbolo no provedor')).toHaveValue(ticker);
  await asset.getByRole('button', { name: 'Cadastrar e adicionar' }).click();
  await expect(page.getByRole('heading', { name: ticker })).toBeVisible();
  const assetPage = page.url();

  await syncMarketData(page);
  await page.getByRole('button', { name: 'Nova movimentação' }).click();
  const movement = page.getByRole('form', { name: 'Movimentação' });
  await movement.getByLabel('Quantidade').fill('100');
  await movement.getByLabel('Preço unitário').fill('10');
  await movement.getByRole('button', { name: 'Registrar movimentação' }).click();
  await expect(movement).toBeHidden();
  await expect(summaryFigure(page, 'Valor')).toHaveText('R$ 1.000,00');

  await page.goto('/market-data');
  await page.getByLabel('Buscar ativo').fill(ticker);
  await page.getByRole('button', { name: 'Buscar' }).click();
  const row = page.locator('[data-testid^="market-asset-"]');
  await expect(row).toHaveCount(1);
  await row.getByRole('button', { name: `Editar ${ticker}` }).click();

  const edit = page.getByRole('form', { name: 'Editar fonte do ativo' });
  await expect(edit.getByRole('heading', { name: `Editar fonte de ${ticker}` })).toBeVisible();
  await edit.getByLabel('Provedor', { exact: true }).selectOption('Yahoo');
  await expect(edit.getByLabel('Símbolo no provedor')).toHaveValue(`${ticker}.SA`);
  await edit.getByRole('button', { name: 'Salvar' }).click();
  await expect(edit).toBeHidden();
  await expect(row).toContainText(`Yahoo Finance (${ticker}.SA)`);
  await expect(row).toContainText('Histórico completo na próxima sincronização.');

  // Started outside the screen, so the screen is read again.
  await syncMarketData(page);
  await page.reload();
  await page.getByLabel('Buscar ativo').fill(ticker);
  await page.getByRole('button', { name: 'Buscar' }).click();
  await expect(row).toContainText(`Yahoo Finance (${ticker}.SA)`);
  await expect(row).not.toContainText('Histórico completo na próxima sincronização.');

  // The rebuild after the sync revalues the position at Yahoo's close; it ends just after the run reads Succeeded.
  await page.goto(assetPage);
  await expect(async () => {
    await page.reload();
    await expect(summaryFigure(page, 'Valor')).toHaveText('R$ 1.250,00', { timeout: 1_000 });
  }).toPass({ timeout: 15_000 });
});

/** Spec E2E test 26. */
test('the portfolio offers no index or exchange rate, and its search leaves them out', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-yahoo-hold'), 'Ada Lovelace');
  await page.goto('/market-data');
  const fields = registration(page);
  const index = uniqueTicker('YX');
  await fields.ticker.fill(index);
  await fields.assetClass.selectOption('Index');
  await fields.submit.click();
  await expect(fields.ticker).toHaveValue('');

  await page.goto('/investments');
  await page.getByLabel('Buscar no catálogo').fill(index);
  await page.getByRole('button', { name: 'Buscar' }).click();
  await expect(page.getByText('Nenhum ativo corresponde a esta busca. Cadastre-o abaixo.')).toBeVisible();

  await page.getByRole('button', { name: 'Cadastrar novo ativo' }).click();
  const classes = page.getByRole('form', { name: 'Cadastrar e adicionar ativo' }).getByLabel('Classe').locator('option');
  await expect(classes).toHaveText(['Ação (B3)', 'Fundo imobiliário', 'ETF (B3)', 'BDR', 'Ação (EUA)', 'Criptomoeda']);
});
