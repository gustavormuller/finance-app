import { expect, test } from '@playwright/test';

import { devLogin, syncMarketData, uniqueEmail, uniqueTicker, utcDaysAgo } from './support';

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

/** `2026-09-13` as the screens write it, `13/09/2026`. */
const shown = (day: string) => day.split('-').reverse().join('/');

/**
 * Spec E2E test 36, and 016's test 14 on the way. 024: the hero's No ano, a custom period
 * and its refusal, and the benchmark table.
 */
test('a buy 30 days back shows a non-zero TWR and the comparison chart', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-returns'), 'Ada Lovelace');

  const ticker = uniqueTicker('RET');
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

  // Spec 016 E2E test 14: the investments page leads with the same figures.
  await page.getByRole('link', { name: '← Investimentos' }).click();
  const hero = page.getByTestId('returns-hero');
  await expect(hero.getByTestId('hero-twr')).toHaveText('+2,99%');
  await expect(hero.getByTestId('hero-xirr')).toContainText('+43,05% a.a.');
  await expect(hero.getByTestId('comparison-chart')).toBeVisible();
  await expect(hero).toContainText(`desde ${shown(utcDaysAgo(30))}`);

  // No ano starts on the 1st of January (the API's UTC year), or at the buy if it is later.
  const january = `${new Date().getUTCFullYear()}-01-01`;
  await hero.getByRole('button', { name: 'No ano' }).click();
  await expect(hero.getByRole('button', { name: 'No ano' })).toHaveAttribute('aria-pressed', 'true');
  await expect(hero).toContainText(`desde ${shown(utcDaysAgo(30) > january ? utcDaysAgo(30) : january)}`);

  await page.getByRole('link', { name: 'Rentabilidade' }).click();
  await expect(page.getByRole('heading', { name: 'Rentabilidade' })).toBeVisible();

  // 971 on the base day to 1 000 today: 1000/971 - 1 = 2,9866%. Buying at the close with
  // no fees, the XIRR is that return over 30 days, a year's rate: (1000/971)^(365/30) - 1.
  await expect(page.getByTestId('returns-period')).toContainText('30 dias');
  await expect(page.getByTestId('headline-twr').getByTestId('headline-value')).toContainText('+2,99%');
  // Under a year the TWR is the period's alone; the XIRR is always a year's rate.
  await expect(page.getByTestId('headline-twr')).not.toContainText('a.a.');
  await expect(page.getByTestId('headline-xirr').getByTestId('headline-value')).toHaveText('+43,05% a.a.');
  await expect(page.getByTestId('benchmark-row-portfolio')).toContainText('+2,99%');

  // The chart, with the portfolio's line drawn.
  const chart = page.getByTestId('comparison-chart');
  await expect(chart).toBeVisible();
  await expect(chart.locator('.recharts-line path')).not.toHaveCount(0);

  // Every benchmark has its row beside the portfolio's. Their figures depend on how many
  // days of fake series the database has kept, so only the rows are asserted.
  for (const code of ['CDI', 'SELIC', 'IPCA6', 'USDBRL', 'IVVB11']) {
    await expect(page.getByTestId(`benchmark-row-${code}`)).toBeVisible();
  }

  // A reference taken off the chart and put back. CDI has a value from every sync.
  const references = page.getByRole('group', { name: 'Referências no gráfico' });
  const lines = chart.locator('.recharts-line');
  await expect.poll(() => lines.count()).toBeGreaterThanOrEqual(2);
  const drawn = await lines.count();
  await references.getByRole('button', { name: 'CDI' }).click();
  await expect(references.getByRole('button', { name: 'CDI' })).toHaveAttribute('aria-pressed', 'false');
  await expect(lines).toHaveCount(drawn - 1);
  await references.getByRole('button', { name: 'CDI' }).click();
  await expect(lines).toHaveCount(drawn);

  // A period change refetches, and 12 months clamps to the buy.
  await page.getByRole('button', { name: '12 meses' }).click();
  await expect(page.getByRole('button', { name: '12 meses' })).toHaveAttribute('aria-pressed', 'true');
  await expect(page.getByTestId('headline-twr').getByTestId('headline-value')).toContainText('+2,99%');

  // A custom period with no flow in it: 9,80 on its base day (21 days back) to 9,91.
  await page.getByRole('button', { name: 'Personalizado' }).click();
  const custom = page.getByRole('form', { name: 'Período personalizado' });
  await custom.getByLabel('De').fill(utcDaysAgo(20));
  await custom.getByLabel('Até').fill(utcDaysAgo(10));
  await custom.getByRole('button', { name: 'Aplicar' }).click();
  await expect(page.getByTestId('returns-period')).toHaveText(`${shown(utcDaysAgo(20))} a ${shown(utcDaysAgo(10))} · 11 dias`);
  await expect(page.getByTestId('headline-twr').getByTestId('headline-value')).toContainText('+1,12%');

  // One day is written in the singular.
  await custom.getByLabel('De').fill(utcDaysAgo(10));
  await custom.getByRole('button', { name: 'Aplicar' }).click();
  await expect(page.getByTestId('returns-period')).toHaveText(`${shown(utcDaysAgo(10))} a ${shown(utcDaysAgo(10))} · 1 dia`);

  // Dates the wrong way round are refused under the first, never with the 400's English title.
  await custom.getByLabel('De').fill(utcDaysAgo(5));
  await custom.getByRole('button', { name: 'Aplicar' }).click();
  await expect(custom.getByRole('alert')).toHaveText('A data inicial deve ser anterior ou igual à data final.');
  await expect(page.getByText('One or more validation errors occurred.')).toHaveCount(0);

  await page.getByRole('button', { name: 'Desde o início' }).click();
  await expect(page.getByTestId('headline-twr').getByTestId('headline-value')).toContainText('+2,99%');

  // The asset's own row, and its own page. A BRL asset has no FX split.
  await page.getByRole('link', { name: ticker }).click();
  await expect(page.getByTestId('headline-twr').getByTestId('headline-value')).toContainText('+2,99%');
  await expect(page.getByTestId('comparison-chart')).toBeVisible();
  await expect(page.getByTestId('fx-split')).toHaveCount(0);
});

/** 024: with nothing held there is no period to measure. */
test('with nothing held, the returns page has nothing to measure', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-returns-empty'), 'Grace Hopper');
  await page.goto('/investments/returns');

  await expect(page.getByText('Nenhuma posição valorizada neste período.')).toBeVisible();
  await expect(page.getByTestId('returns-period')).toHaveCount(0);
});
