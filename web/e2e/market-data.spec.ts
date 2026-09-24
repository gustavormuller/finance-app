import { expect, test, type Page } from '@playwright/test';

import { devLogin, uniqueEmail } from './support';

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
function uniqueTicker() {
  return `E2E${crypto.randomUUID().replaceAll('-', '').slice(0, 8).toUpperCase()}`;
}

async function registerAsset(page: Page, ticker: string, name: string) {
  const form = page.getByRole('form', { name: 'Cadastrar ativo' });
  await form.getByLabel('Ticker').fill(ticker);
  await form.getByLabel('Nome (opcional)').fill(name);
  await form.getByLabel('Classe').selectOption('StockBr');
  // Exact: "Símbolo no provedor" contains the word too.
  await form.getByLabel('Provedor', { exact: true }).selectOption('Brapi');
  await form.getByLabel('Símbolo no provedor').fill(ticker);
  await form.getByLabel('Moeda').selectOption('BRL');
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

/** Spec E2E test 26. */
test('a manual sync on the fake providers appears as Succeeded, by provider', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-sync'), 'Ada Lovelace');
  await page.goto('/market-data');

  // At least one asset, so brapi is in the summary whatever the catalogue held before.
  await registerAsset(page, uniqueTicker(), 'Ativo sincronizado E2E');
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
  await expect(run.getByTestId('provider-summary-Bcb')).toContainText('Banco Central (SGS)');
  await expect(run.getByText('com falha')).toHaveCount(0);

  // Newest first.
  await expect(page.locator('[data-testid^="sync-run-"]').first()).toHaveAttribute('data-testid', `sync-run-${syncRunId}`);
});
