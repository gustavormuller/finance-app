import { expect, test, type Page } from '@playwright/test';

import { devLogin, syncMarketData, uniqueEmail } from './support';

/**
 * Spec 026 E2E (tests 12–14), against the real API process and a real PostgreSQL.
 *
 * The API runs with `MarketData:FakeProviders=true`. A new brapi or CoinGecko ticker's first
 * sync stores a close for every day of the last five years: 10 on yesterday (UTC), a cent
 * lower for each day before it. The benchmark fake stores 0,05 on every day, so CDI grows
 * 0,05% a day and PTAX is a constant R$ 0,05.
 *
 * The catalogue and the series are shared and kept between runs, so the tickers are new on
 * every run. A benchmark's stored history is the database's: a long-lived one holds what
 * earlier builds wrote, and where a series' history begins can move a comparison's start.
 * So the period and the figures are asserted while only this run's assets are compared, and a
 * benchmark joins afterwards, asserted by its row and its line (spec 024).
 *
 * The second test syncs, so this file runs in its own Playwright project after `returns`
 * (playwright.config.ts), one test after another: the later ones reuse the second's tickers.
 */
test.describe.configure({ mode: 'serial' });

const tickers = { brl: '', usd: '' };

/** A ticker no earlier run registered. */
function uniqueTicker(prefix: string) {
  return `${prefix}${crypto.randomUUID().replaceAll('-', '').slice(0, 8).toUpperCase()}`;
}

/** `days` before today in UTC, the API's calendar, as `YYYY-MM-DD`. */
function utcDaysAgo(days: number) {
  return new Date(Date.now() - days * 86_400_000).toISOString().slice(0, 10);
}

/** A catalogue entry through `/market-data`'s form. */
async function registerAsset(page: Page, ticker: string, provider: string, assetClass: string, currency: string) {
  const form = page.getByRole('form', { name: 'Cadastrar ativo' });
  await form.getByLabel('Ticker').fill(ticker);
  await form.getByLabel('Classe').selectOption(assetClass);
  // Exact: "Símbolo no provedor" contains the word too.
  await form.getByLabel('Provedor', { exact: true }).selectOption(provider);
  await form.getByLabel('Símbolo no provedor').fill(ticker.toLowerCase());
  await form.getByLabel('Moeda').selectOption(currency);
  await form.getByRole('button', { name: 'Cadastrar ativo' }).click();
  await expect(form.getByLabel('Ticker')).toHaveValue('');
}

/** A series found by search and added. */
async function pick(page: Page, query: string, label: string) {
  await page.getByRole('searchbox', { name: 'Buscar ativo ou índice' }).fill(query);
  await page.getByRole('button', { name: `Adicionar ${label}` }).click();
  await expect(page.getByRole('button', { name: `Remover ${label}` })).toBeVisible();
}

/** A custom period of ten days, ending `toDaysAgo` days before today (UTC). */
async function tenDaysEnding(page: Page, toDaysAgo: number) {
  await page.getByRole('group', { name: 'Período' }).getByRole('button', { name: 'Personalizado' }).click();
  await page.getByLabel('De', { exact: true }).fill(utcDaysAgo(toDaysAgo + 10));
  await page.getByLabel('Até', { exact: true }).fill(utcDaysAgo(toDaysAgo));
  await page.getByRole('button', { name: 'Aplicar' }).click();
  await expect(page.getByTestId('compare-period')).toContainText('10 dias');
}

/** The last ten days, as a custom period. */
const lastTenDays = (page: Page) => tenDaysEnding(page, 1);

function row(page: Page, name: string) {
  return page.getByTestId('compare-table').getByRole('row', { name: new RegExp(name) });
}

function lines(page: Page) {
  return page.getByTestId('compare-chart').locator('.recharts-line path');
}

/** Spec E2E test 12. */
test('fewer than two series ask for another, and nothing is drawn', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-compare-few'), 'Ada Lovelace');
  await page.goto('/');
  await page.getByRole('navigation', { name: 'Principal' }).getByRole('link', { name: 'Comparar' }).click();
  await expect(page.getByRole('heading', { name: 'Comparar', level: 2 })).toBeVisible();
  await expect(page.getByTestId('compare-empty')).toContainText('Escolha pelo menos duas séries para comparar.');

  await pick(page, 'cdi', 'CDI');

  await expect(page.getByTestId('compare-empty')).toBeVisible();
  await expect(page.getByTestId('compare-chart')).toHaveCount(0);
  await expect(page).toHaveURL(/series=benchmark%3ACDI/);

  await page.getByRole('button', { name: 'Remover CDI' }).click();
  await expect(page.getByRole('button', { name: 'Remover CDI' })).toHaveCount(0);
  await expect(page.getByTestId('compare-empty')).toBeVisible();
});

/** Spec E2E test 13. */
test('two catalogue assets and CDI side by side, over ten days and then a year', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-compare'), 'Ada Lovelace');
  tickers.brl = uniqueTicker('CMB');
  tickers.usd = uniqueTicker('CMU');
  await page.goto('/market-data');
  await registerAsset(page, tickers.brl, 'Brapi', 'StockBr', 'BRL');
  await registerAsset(page, tickers.usd, 'CoinGecko', 'Crypto', 'USD');
  await syncMarketData(page);

  await page.goto('/compare');
  await pick(page, tickers.brl, tickers.brl);
  await pick(page, tickers.usd, tickers.usd);
  await lastTenDays(page);

  // 9,90 eleven days back, 10,00 yesterday: +1,01% for both, each in its own currency.
  await expect(row(page, tickers.brl)).toContainText('+1,01%');
  await expect(row(page, tickers.brl)).toContainText('R$');
  await expect(row(page, tickers.usd)).toContainText('+1,01%');
  await expect(row(page, tickers.usd)).toContainText('US$');
  await expect(lines(page)).toHaveCount(2);
  await expect(page).toHaveURL(new RegExp(`period=custom&from=${utcDaysAgo(11)}&to=${utcDaysAgo(1)}`));
  const tenDays = (await page.getByTestId('compare-period').textContent()) ?? '';

  // CDI joins. Where its stored history begins is the database's, so its row and its line
  // are asserted, not its figure or the period it leaves.
  await pick(page, 'CDI', 'CDI');
  await expect(row(page, 'CDI')).toContainText('%');
  await expect(lines(page)).toHaveCount(3);
  await expect(page).toHaveURL(/period=custom/);

  // A preset refetches, and goes into the URL.
  const periods = page.getByRole('group', { name: 'Período' });
  await periods.getByRole('button', { name: '1A' }).click();
  await expect(periods.getByRole('button', { name: '1A' })).toHaveAttribute('aria-pressed', 'true');
  await expect(page).toHaveURL(/period=1y/);
  await expect(page.getByTestId('compare-period')).not.toHaveText(tenDays);
  await expect(lines(page)).toHaveCount(3);

  // The log scale is a setting of the URL as well.
  await page.getByRole('group', { name: 'Escala' }).getByRole('button', { name: 'Log' }).click();
  await expect(page).toHaveURL(/scale=log/);
  await expect(lines(page)).toHaveCount(3);
});

/** Spec E2E test 14. */
test('converting to reais and to dollars, and the address opened again', async ({ page, context }) => {
  await devLogin(page, uniqueEmail('e2e-compare-fx'), 'Ada Lovelace');
  await page.goto('/compare');
  await pick(page, tickers.brl, tickers.brl);
  await pick(page, tickers.usd, tickers.usd);
  await lastTenDays(page);
  const currency = page.getByRole('group', { name: 'Moeda' });

  // PTAX is constant in the fakes, so a converted change is the same change.
  await currency.getByRole('button', { name: 'Converter para R$' }).click();
  await expect(page).toHaveURL(/currency=BRL/);
  await expect(row(page, tickers.usd)).toContainText('convertido para R$');
  await expect(row(page, tickers.usd)).toContainText('+1,01%');
  await expect(row(page, tickers.brl)).not.toContainText('convertido');

  await currency.getByRole('button', { name: 'Converter para US$' }).click();
  await expect(page).toHaveURL(/currency=USD/);
  await expect(row(page, tickers.brl)).toContainText('convertido para US$');
  await expect(row(page, tickers.brl)).toContainText('+1,01%');
  await expect(row(page, tickers.usd)).not.toContainText('convertido');

  // CDI, a series in reais, is converted as well; its figure is the database's (test 13).
  await pick(page, 'CDI', 'CDI');
  await expect(row(page, 'CDI')).toContainText('convertido para US$');

  // The address alone rebuilds the same comparison.
  const period = (await page.getByTestId('compare-period').textContent()) ?? '';
  const table = (await page.getByTestId('compare-table').textContent()) ?? '';
  const bookmarked = await context.newPage();
  await bookmarked.goto(page.url());
  await expect(bookmarked.getByRole('group', { name: 'Moeda' }).getByRole('button', { name: 'Converter para US$' })).toHaveAttribute(
    'aria-pressed',
    'true',
  );
  await expect(bookmarked.getByRole('group', { name: 'Período' }).getByRole('button', { name: 'Personalizado' })).toHaveAttribute(
    'aria-pressed',
    'true',
  );
  await expect(bookmarked.getByTestId('compare-period')).toHaveText(period);
  await expect(bookmarked.getByTestId('compare-table')).toHaveText(table);
  await expect(bookmarked.getByRole('button', { name: `Remover ${tickers.brl}` })).toBeVisible();
});

/**
 * Spec 024: the search's other answers, the six-series ceiling, and what the page says when
 * the period holds nothing or is refused. Runs after test 13's sync, so the ticker it
 * registers stays without closes until a later sync.
 */
test('the search, six series at most, a period with no data, a refused period and a series left out', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-compare-limits'), 'Grace Hopper');
  const unpriced = uniqueTicker('CMN');
  await page.goto('/market-data');
  await registerAsset(page, unpriced, 'Brapi', 'StockBr', 'BRL');

  await page.goto('/compare');
  const search = page.getByRole('searchbox', { name: 'Buscar ativo ou índice' });
  await search.fill(`${unpriced}X`);
  await expect(page.getByText('Nada encontrado com esse ticker ou nome.')).toBeVisible();

  // Accents and case do not matter, and a series already chosen is not offered twice.
  await pick(page, 'dolar', 'Dólar (PTAX)');
  await search.fill('DÓLAR');
  await expect(page.getByRole('button', { name: 'Dólar (PTAX), já escolhida' })).toBeDisabled();

  // A series with no data in the period is left out of the chart, and says so.
  await pick(page, unpriced, unpriced);
  await expect(page.getByText(`Sem dados no período escolhido, fora do gráfico: ${unpriced}.`)).toBeVisible();
  await expect(row(page, unpriced)).toContainText('Sem dados no período');

  // Six at most.
  for (const benchmark of ['CDI', 'SELIC', 'IPCA', 'S&P 500 (IVVB11)']) {
    await pick(page, benchmark.split(' ')[0]!, benchmark);
  }
  await expect(search).toBeDisabled();
  await expect(page.getByText('Até 6 séries por comparação. Remova uma para escolher outra.')).toBeVisible();
  await page.getByRole('button', { name: 'Remover IPCA' }).click();
  await expect(search).toBeEnabled();

  // Before any series begins (the BCB's reach back to 1994 since 025), nothing has data.
  const periods = page.getByRole('group', { name: 'Período' });
  await periods.getByRole('button', { name: 'Personalizado' }).click();
  await page.getByLabel('De', { exact: true }).fill('1960-01-01');
  await page.getByLabel('Até', { exact: true }).fill('1960-01-31');
  await page.getByRole('button', { name: 'Aplicar' }).click();
  await expect(page.getByText('Nenhuma das séries tem dados no período escolhido.')).toBeVisible();
  await expect(page.getByText('Escolha um período mais longo, ou Máx.')).toBeVisible();

  // Dates the wrong way round are the API's refusal, in Portuguese.
  await periods.getByRole('button', { name: 'Personalizado' }).click();
  await page.getByLabel('De', { exact: true }).fill('1960-02-01');
  await page.getByRole('button', { name: 'Aplicar' }).click();
  await expect(page.getByRole('alert')).toHaveText('A data inicial deve ser anterior à data final.');
  await expect(page.getByText('One or more validation errors occurred.')).toHaveCount(0);
});

/**
 * Spec 024, for 025: a Yahoo asset is compared by its total return. The Yahoo fake pays a
 * dividend 30 days before the sync's `to` (yesterday), worth 1 % of the price. From 36 to 26
 * days back its price goes from 12,15 to 12,25 (+0,82%); its adjusted close, the dividend
 * reinvested, from 12,0285 to 12,25 (+1,84%). Over the same days brapi's fake goes from 9,65
 * to 9,75 (+1,04%). Both series are this run's, and so is where each begins.
 */
test('a Yahoo asset is compared by its total return, and a start moved to its first day says so', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-compare-yahoo'), 'Katherine Johnson');
  const yahoo = uniqueTicker('CMY');
  await page.goto('/market-data');
  // Yahoo, `….SA` and BRL are what the form suggests for a B3 stock.
  const form = page.getByRole('form', { name: 'Cadastrar ativo' });
  await form.getByLabel('Ticker').fill(yahoo);
  await expect(form.getByLabel('Símbolo no provedor')).toHaveValue(`${yahoo}.SA`);
  await form.getByRole('button', { name: 'Cadastrar ativo' }).click();
  await expect(form.getByLabel('Ticker')).toHaveValue('');
  await syncMarketData(page);

  await page.goto('/compare');
  await pick(page, yahoo, yahoo);
  await pick(page, tickers.brl, tickers.brl);
  await tenDaysEnding(page, 26);

  await expect(row(page, yahoo)).toContainText('+1,84%');
  await expect(row(page, yahoo)).toContainText('R$');
  await expect(row(page, tickers.brl)).toContainText('+1,04%');

  // Over 5A the Yahoo fake's 1000 days begin after the period does, and brapi's five years do
  // not: the start moves to the Yahoo asset's first day, and the page says so.
  await page.getByRole('group', { name: 'Período' }).getByRole('button', { name: '5A' }).click();
  const firstDay = utcDaysAgo(1001).split('-').reverse().join('/');
  await expect(page.getByText(`Começa em ${firstDay}, primeiro dia com dados de ${yahoo}.`)).toBeVisible();
});
