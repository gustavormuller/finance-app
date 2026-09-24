import { expect, test } from '@playwright/test';

import { devLogin, syncMarketData, uniqueEmail } from './support';

/**
 * Spec 008 E2E (test 36), against the real API process and a real PostgreSQL.
 *
 * The API runs with `MarketData:FakeProviders=true` (scripts/verify-e2e.sh). A fake close is
 * 10 on the sync's `to` (yesterday, UTC) and one cent lower for each day before it, so a buy
 * 30 days back meets a close of 9,71 and the price rises to 10 after it.
 *
 * The ticker is new on every run. The catalogue is kept between runs, and the sync only
 * fetches days after an asset's latest stored close, so a reused ticker (PETR4) would carry
 * closes stored by earlier runs, on other days' shapes. A new one is backfilled in full by
 * this run's sync.
 *
 * The test syncs, so it runs in its own Playwright project after the investments project
 * (playwright.config.ts): it never races test 26's 202, and it waits out a 429.
 */

/** A ticker no earlier run registered. */
function uniqueTicker() {
  return `RET${crypto.randomUUID().replaceAll('-', '').slice(0, 8).toUpperCase()}`;
}

/** `days` before today in UTC, the API's calendar, as `YYYY-MM-DD`. */
function utcDaysAgo(days: number) {
  return new Date(Date.now() - days * 86_400_000).toISOString().slice(0, 10);
}

/** Spec E2E test 36. */
test('a buy 30 days back shows a non-zero TWR and the comparison chart', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-returns'), 'Ada Lovelace');

  const ticker = uniqueTicker();
  await page.goto('/investments');
  await page.getByRole('button', { name: 'Cadastrar novo ativo' }).click();
  const asset = page.getByRole('form', { name: 'Cadastrar e adicionar ativo' });
  await asset.getByLabel('Ticker').fill(ticker);
  await asset.getByLabel('Provedor', { exact: true }).selectOption('Brapi');
  await asset.getByLabel('Símbolo no provedor').fill(ticker);
  await asset.getByRole('button', { name: 'Cadastrar e adicionar' }).click();
  await expect(page.getByRole('heading', { name: ticker })).toBeVisible();

  await syncMarketData(page);

  // 100 at the day's close, 9,71, with no fees: the value on the base day is 971.
  await page.getByRole('button', { name: 'Nova movimentação' }).click();
  const movement = page.getByRole('form', { name: 'Movimentação' });
  await movement.getByLabel('Data').fill(utcDaysAgo(30));
  await movement.getByLabel('Quantidade').fill('100');
  await movement.getByLabel('Preço unitário').fill('9,71');
  await movement.getByRole('button', { name: 'Registrar movimentação' }).click();
  await expect(movement).toBeHidden();

  await page.getByRole('link', { name: '← Investimentos' }).click();
  await page.getByRole('link', { name: 'Rentabilidade' }).click();
  await expect(page.getByRole('heading', { name: 'Rentabilidade' })).toBeVisible();

  // 971 on the base day to 1 000 today: 1000/971 - 1 = 2,9866%. Buying at the close with
  // no fees, the XIRR is that return over 30 days, a year's rate: (1000/971)^(365/30) - 1.
  await expect(page.getByTestId('returns-period')).toContainText('30 dias');
  await expect(page.getByTestId('headline-twr').getByTestId('headline-value')).toContainText('+2,99%');
  await expect(page.getByTestId('headline-xirr').getByTestId('headline-value')).toHaveText('+43,05% a.a.');
  await expect(page.getByTestId('benchmark-row-portfolio')).toContainText('+2,99%');

  // The chart, with the portfolio's line drawn.
  const chart = page.getByTestId('comparison-chart');
  await expect(chart).toBeVisible();
  await expect(chart.locator('.recharts-line path')).not.toHaveCount(0);

  // A period change refetches, and 12 months clamps to the buy.
  await page.getByRole('button', { name: '12 meses' }).click();
  await expect(page.getByRole('button', { name: '12 meses' })).toHaveAttribute('aria-pressed', 'true');
  await expect(page.getByTestId('headline-twr').getByTestId('headline-value')).toContainText('+2,99%');

  // The asset's own row, and its own page.
  await page.getByRole('link', { name: ticker }).click();
  await expect(page.getByTestId('headline-twr').getByTestId('headline-value')).toContainText('+2,99%');
  await expect(page.getByTestId('comparison-chart')).toBeVisible();
});
