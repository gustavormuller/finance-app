import { expect, test, type Page } from '@playwright/test';

import { devLogin, syncMarketData, uniqueEmail } from './support';

/**
 * Spec 026 E2E (tests 12–14), against the real API process and a real PostgreSQL.
 *
 * The API runs with `MarketData:FakeProviders=true`. A new ticker's first sync stores a close
 * for every day of the last five years: 10 on yesterday (UTC), a cent lower for each day before
 * it. The benchmark fake stores 0,05 on every day, so CDI grows 0,05% a day and PTAX is a
 * constant R$ 0,05. The catalogue and the series are shared and kept between runs, so the
 * tickers are new on every run, and the figures asserted are the assets' over the last ten
 * days, which this run's sync wrote; CDI's history depends on earlier runs, so only its row is.
 *
 * The second test syncs, so this file runs in its own Playwright project after `returns`
 * (playwright.config.ts), one test after another: the third reuses the second's tickers.
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

/** The last ten days, as a custom period. */
async function lastTenDays(page: Page) {
  await page.getByRole('group', { name: 'Período' }).getByRole('button', { name: 'Personalizado' }).click();
  await page.getByLabel('De', { exact: true }).fill(utcDaysAgo(11));
  await page.getByLabel('Até', { exact: true }).fill(utcDaysAgo(1));
  await page.getByRole('button', { name: 'Aplicar' }).click();
  await expect(page.getByTestId('compare-period')).toContainText('10 dias');
}

function row(page: Page, name: string) {
  return page.getByTestId('compare-table').getByRole('row', { name: new RegExp(name) });
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
  await pick(page, 'CDI', 'CDI');
  await lastTenDays(page);

  // 9,90 eleven days back, 10,00 yesterday: +1,01% for both, each in its own currency.
  await expect(row(page, tickers.brl)).toContainText('+1,01%');
  await expect(row(page, tickers.brl)).toContainText('R$');
  await expect(row(page, tickers.usd)).toContainText('+1,01%');
  await expect(row(page, tickers.usd)).toContainText('US$');
  await expect(row(page, 'CDI')).toContainText('%');
  await expect(page.getByTestId('compare-chart').locator('.recharts-line path')).toHaveCount(3);
  await expect(page).toHaveURL(new RegExp(`period=custom&from=${utcDaysAgo(11)}&to=${utcDaysAgo(1)}`));

  // A preset refetches, and goes into the URL.
  const periods = page.getByRole('group', { name: 'Período' });
  await periods.getByRole('button', { name: '1A' }).click();
  await expect(periods.getByRole('button', { name: '1A' })).toHaveAttribute('aria-pressed', 'true');
  await expect(page).toHaveURL(/period=1y/);
  await expect(page.getByTestId('compare-period')).not.toContainText('10 dias');
  await expect(page.getByTestId('compare-chart').locator('.recharts-line path')).toHaveCount(3);

  // The log scale is a setting of the URL as well.
  await page.getByRole('group', { name: 'Escala' }).getByRole('button', { name: 'Log' }).click();
  await expect(page).toHaveURL(/scale=log/);
  await expect(page.getByTestId('compare-chart').locator('.recharts-line path')).toHaveCount(3);
});

/** Spec E2E test 14. */
test('converting to reais and to dollars, and the address opened again', async ({ page, context }) => {
  await devLogin(page, uniqueEmail('e2e-compare-fx'), 'Ada Lovelace');
  await page.goto('/compare');
  await pick(page, tickers.brl, tickers.brl);
  await pick(page, tickers.usd, tickers.usd);
  await pick(page, 'CDI', 'CDI');
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
  await expect(row(page, 'CDI')).toContainText('convertido para US$');
  await expect(row(page, tickers.usd)).not.toContainText('convertido');

  // The address alone rebuilds the comparison.
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
  await expect(bookmarked.getByTestId('compare-period')).toContainText('10 dias');
  await expect(row(bookmarked, tickers.brl)).toContainText('+1,01%');
  await expect(row(bookmarked, tickers.usd)).toContainText('+1,01%');
  await expect(row(bookmarked, 'CDI')).toContainText('convertido para US$');
  await expect(bookmarked.getByRole('button', { name: `Remover ${tickers.brl}` })).toBeVisible();
});
