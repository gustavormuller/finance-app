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
  await expect(rows.first()).toContainText('Ação (B3)');
  await expect(rows.first()).toContainText(`brapi (${ticker})`);

  await registerAsset(page, ticker, 'De novo');
  await expect(page.getByRole('alert')).toHaveText(`O símbolo '${ticker}' já está cadastrado no provedor Brapi.`);

  // 019: each provider says what it prices, Yahoo first since 025, and a Binance pair is
  // stored in capitals.
  const form = page.getByRole('form', { name: 'Cadastrar ativo' });
  await expect(form.getByLabel('Provedor', { exact: true }).locator('option')).toHaveText([
    'Yahoo Finance (B3, EUA, índices, cripto em US$, câmbio — sem chave)',
    'brapi (B3: ações, FIIs, ETFs)',
    'CoinGecko (cripto)',
    'Twelve Data (ações dos EUA)',
    'Binance (cripto em reais)',
  ]);
  const pair = uniqueTicker();
  await registerAsset(page, pair, 'Par em minúsculas', { provider: 'Binance', assetClass: 'Crypto', symbol: `${pair.toLowerCase()}brl` });
  await expect(form.getByLabel('Ticker')).toHaveValue('');
  await page.getByLabel('Buscar ativo').fill(pair);
  await page.getByRole('button', { name: 'Buscar' }).click();
  await expect(rows.first()).toContainText(`Binance (${pair}BRL)`);
  await expect(rows.first()).toContainText('Criptomoeda');
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

  // 025: a Yahoo symbol is letters, digits and . - ^ = only.
  await registerAsset(page, uniqueTicker(), 'Yahoo com espaço', { provider: 'Yahoo', symbol: 'PETR4 SA' });
  await expect(under('Símbolo no provedor')).toHaveText('Use um símbolo do Yahoo Finance, como PETR4.SA, AAPL, ^BVSP, BTC-USD ou BRL=X.');
  await expect(page.getByText('One or more validation errors occurred.')).toHaveCount(0);
});

/**
 * 025, decision 18: the symbol and the currency follow the ticker, class and provider, each
 * provider spelling the ticker its own way, until the person sets that field. Nothing is
 * registered: the catalogue is shared.
 */
test('the symbol and the currency are suggested for each provider until the person sets them', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-market-suggest'), 'Katherine Johnson');
  await page.goto('/market-data');
  const form = page.getByRole('form', { name: 'Cadastrar ativo' });
  const provider = form.getByLabel('Provedor', { exact: true });
  const symbol = form.getByLabel('Símbolo no provedor');
  const currency = form.getByLabel('Moeda');

  await form.getByLabel('Ticker').fill('aapl');
  await form.getByLabel('Classe').selectOption('StockUs');
  await expect(provider).toHaveValue('Yahoo');
  const suggested: [string, string, string][] = [
    ['Yahoo', 'AAPL', 'USD'],
    ['Binance', 'AAPLBRL', 'BRL'],
    ['CoinGecko', '', 'USD'],
    ['TwelveData', 'AAPL', 'USD'],
    ['Brapi', 'AAPL', 'BRL'],
  ];
  for (const [kind, expectedSymbol, expectedCurrency] of suggested) {
    await provider.selectOption(kind);
    await expect(symbol, kind).toHaveValue(expectedSymbol);
    await expect(currency, kind).toHaveValue(expectedCurrency);
  }

  // Typed, the symbol is the person's: a new ticker or provider leaves it, and the currency
  // follows what it says.
  await provider.selectOption('Yahoo');
  await symbol.fill('msft');
  await form.getByLabel('Ticker').fill('goog');
  await provider.selectOption('TwelveData');
  await provider.selectOption('Yahoo');
  await expect(symbol).toHaveValue('msft');
  await symbol.fill('ITUB4.SA');
  await expect(currency).toHaveValue('BRL');

  // Chosen, the currency is the person's too.
  await currency.selectOption('USD');
  await form.getByLabel('Classe').selectOption('Fii');
  await symbol.fill('HGLG11.SA');
  await expect(currency).toHaveValue('USD');
});

/**
 * 025, decision 16: Editar moves an entry to another provider or symbol, checked as a
 * registration is, against the entry's own currency; a series already in the catalogue is
 * the sentence; Cancelar leaves the entry as it was. Nothing is saved, so no sync is needed.
 */
test('a source edit is refused under its field or as a duplicate, and Cancelar keeps the entry', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-market-edit'), 'Grace Hopper');
  await page.goto('/market-data');
  const ticker = uniqueTicker();
  const taken = uniqueTicker();
  await registerAsset(page, ticker, 'Fonte a editar');
  await expect(page.getByLabel('Ticker')).toHaveValue('');
  await registerAsset(page, taken, 'Já no Yahoo', { provider: 'Yahoo', symbol: `${taken}.SA` });
  await expect(page.getByLabel('Ticker')).toHaveValue('');

  await page.getByLabel('Buscar ativo').fill(ticker);
  await page.getByRole('button', { name: 'Buscar' }).click();
  const row = page.locator('[data-testid^="market-asset-"]');
  await expect(row).toHaveCount(1);
  await row.getByRole('button', { name: `Editar ${ticker}` }).click();

  const edit = page.getByRole('form', { name: 'Editar fonte do ativo' });
  await expect(edit).toContainText(
    `A próxima sincronização troca as cotações de ${ticker} pelo histórico completo da nova fonte. A moeda (BRL) não muda.`,
  );
  const symbol = edit.getByLabel('Símbolo no provedor');
  // Page-rooted: `has` is matched inside each of the form's divs.
  const under = edit.locator('div').filter({ has: page.getByLabel('Símbolo no provedor', { exact: true }) }).getByRole('alert');
  await expect(symbol).toHaveValue(ticker);
  await edit.getByLabel('Provedor', { exact: true }).selectOption('Yahoo');
  await expect(symbol).toHaveValue(`${ticker}.SA`);

  await symbol.fill(`${ticker}-USD`);
  await edit.getByRole('button', { name: 'Salvar' }).click();
  await expect(under).toHaveText(`O símbolo ${ticker}-USD é cotado em USD no Yahoo Finance. Este ativo é cotado em BRL.`);

  await symbol.fill(`${ticker} SA`);
  await edit.getByRole('button', { name: 'Salvar' }).click();
  await expect(under).toHaveText('Use um símbolo do Yahoo Finance, como PETR4.SA, AAPL, ^BVSP, BTC-USD ou BRL=X.');

  await symbol.fill(`${taken.toLowerCase()}.sa`);
  await edit.getByRole('button', { name: 'Salvar' }).click();
  await expect(edit.getByRole('alert')).toHaveText(`O símbolo '${taken}.SA' já está cadastrado no provedor Yahoo.`);
  await expect(page.getByText('One or more validation errors occurred.')).toHaveCount(0);

  await edit.getByRole('button', { name: 'Cancelar' }).click();
  await expect(edit).toBeHidden();
  await expect(row).toContainText(`brapi (${ticker})`);
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
  await expect(run.getByTestId('provider-summary-Brapi')).toContainText(/ite(m|ns) sincronizados?/);
  await expect(run.getByTestId('provider-summary-Binance')).toContainText(/linhas? gravadas?/);
  await expect(run.getByTestId('provider-summary-Bcb')).toContainText('Banco Central (SGS)');
  await expect(run.getByText('com falha')).toHaveCount(0);

  // Newest first.
  await expect(page.locator('[data-testid^="sync-run-"]').first()).toHaveAttribute('data-testid', `sync-run-${syncRunId}`);
});
