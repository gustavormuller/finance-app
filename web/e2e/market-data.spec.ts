import { expect, test, type Page } from '@playwright/test';

import { devLogin, uniqueEmail, uniqueTicker as uniqueTickerWith } from './support';

/**
 * Spec 006 E2E, against the real API process and a real PostgreSQL.
 *
 * The API runs with `MarketData:FakeProviders=true` (scripts/verify-e2e.sh): the sync
 * uses fixed, network-free providers, and the ten-minute window between manual syncs is
 * zero, because the catalogue and the sync runs are shared by every test and kept
 * between runs. Only the sync test triggers a sync here. The investments spec also syncs,
 * and runs only after this file has finished (playwright.config.ts), so nothing races
 * test 26 for the sync gate.
 */

/** A ticker no earlier run registered: the catalogue is shared and never emptied. */
const uniqueTicker = () => uniqueTickerWith('E2E');

async function registerAsset(
  page: Page,
  ticker: string,
  name: string,
  { provider = 'Brapi', assetClass = 'StockBr', symbol = ticker, currency = 'BRL' } = {},
) {
  const form = page.getByRole('form', { name: 'Cadastrar ativo' });
  await form.getByLabel('Ticker').fill(ticker);
  await form.getByLabel('Nome (opcional)').fill(name);
  await form.getByLabel('Classe').selectOption(assetClass);
  // Exact: "Símbolo no provedor" contains the word too.
  await form.getByLabel('Provedor', { exact: true }).selectOption(provider);
  await form.getByLabel('Símbolo no provedor').fill(symbol);
  await form.getByLabel('Moeda').selectOption(currency);
  await form.getByRole('button', { name: 'Cadastrar ativo' }).click();
}

test('registers a ticker, finds it by search, and refuses it twice', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-market'), 'Ada Lovelace');
  await page.goto('/market-data');
  await expect(page.getByRole('heading', { name: 'Dados de mercado' })).toBeVisible();

  const ticker = uniqueTicker();
  await registerAsset(page, ticker, 'Ativo de teste E2E');
  await expect(page.getByLabel('Ticker')).toHaveValue('');

  await page.getByLabel('Buscar ativo').fill(ticker.toLowerCase());
  await page.getByRole('button', { name: 'Buscar' }).click();
  const rows = page.locator('[data-testid^="market-asset-"]');
  await expect(rows).toHaveCount(1);
  await expect(rows.first()).toContainText(ticker);
  await expect(rows.first()).toContainText('Ativo de teste E2E');

  await registerAsset(page, ticker, 'De novo');
  await expect(page.getByRole('alert')).toHaveText(`O símbolo '${ticker}' já está cadastrado no provedor Brapi.`);
});

/** 024: the API's rules for a pair or a currency a provider cannot price, each under its field. */
test('a registration a provider cannot price is refused under the field that is wrong', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-market-refused'), 'Grace Hopper');
  await page.goto('/market-data');
  const form = page.getByRole('form', { name: 'Cadastrar ativo' });
  const under = (label: string) => form.locator('div').filter({ has: page.getByLabel(label, { exact: true }) }).getByRole('alert');

  await registerAsset(page, uniqueTicker(), 'Par em dólar', { provider: 'Binance', assetClass: 'Crypto', symbol: 'BTCUSDT' });
  await expect(under('Símbolo no provedor')).toHaveText('Use um par da Binance cotado em reais, terminado em BRL (ex.: BTCBRL).');

  await registerAsset(page, uniqueTicker(), 'Binance em dólar', {
    provider: 'Binance',
    assetClass: 'Crypto',
    symbol: 'BTCBRL',
    currency: 'USD',
  });
  await expect(under('Moeda')).toHaveText('Ativos da Binance são cotados em BRL.');

  await registerAsset(page, uniqueTicker(), 'CoinGecko em reais', { provider: 'CoinGecko', assetClass: 'Crypto', symbol: 'bitcoin' });
  await expect(under('Moeda')).toHaveText('Ativos do CoinGecko são cotados em USD.');
});

test('a search with no match says so', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-market-search'), 'Alan Turing');
  await page.goto('/market-data');

  await page.getByLabel('Buscar ativo').fill(uniqueTicker());
  await page.getByRole('button', { name: 'Buscar' }).click();

  await expect(page.getByText('Nenhum ativo corresponde a esta busca.')).toBeVisible();
});

/** Spec E2E test 26. */
test('a manual sync on the fake providers appears as Succeeded, by provider', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-sync'), 'Ada Lovelace');
  await page.goto('/market-data');

  // At least one asset, so brapi is in the summary whatever the catalogue held before.
  await registerAsset(page, uniqueTicker(), 'Ativo sincronizado E2E');
  await expect(page.getByLabel('Ticker')).toHaveValue('');
  // 019: a Binance pair too, so Binance is in the summary as well.
  const pair = uniqueTicker();
  await registerAsset(page, pair, 'Cripto em reais E2E', { provider: 'Binance', assetClass: 'Crypto', symbol: `${pair}BRL` });
  await expect(page.getByLabel('Ticker')).toHaveValue('');

  const accepted = page.waitForResponse(
    (response) => response.url().endsWith('/api/market-data/sync') && response.request().method() === 'POST',
  );
  await page.getByRole('button', { name: 'Sincronizar agora' }).click();
  const response = await accepted;
  expect(response.status()).toBe(202);
  const { syncRunId } = (await response.json()) as { syncRunId: string };

  // Polled by the screen while Running; the fakes answer at once.
  const run = page.getByTestId(`sync-run-${syncRunId}`);
  await expect(run.getByTestId('sync-run-status')).toHaveText('Concluída', { timeout: 15_000 });
  await expect(run).toContainText('Manual');
  await expect(run.getByTestId('provider-summary-Brapi')).toContainText('brapi');
  await expect(run.getByTestId('provider-summary-Brapi')).toContainText('itens sincronizados');
  await expect(run.getByTestId('provider-summary-Binance')).toContainText(/linhas? gravadas?/);
  await expect(run.getByTestId('provider-summary-Bcb')).toContainText('Banco Central (SGS)');
  await expect(run.getByText('com falha')).toHaveCount(0);

  // Newest first.
  await expect(page.locator('[data-testid^="sync-run-"]').first()).toHaveAttribute('data-testid', `sync-run-${syncRunId}`);
});
