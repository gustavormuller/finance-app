import { expect, test } from '@playwright/test';

import { createAccount, devLogin, uniqueEmail } from './support';

/**
 * Spec 024: the shell around every page. The sidebar's links, the page reached only by its
 * address or a link, and what an address that names nothing shows.
 */

test('the sidebar opens every page and marks the one shown', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-nav'), 'Ada Lovelace');
  await page.goto('/');

  const nav = page.getByRole('navigation', { name: 'Principal' });
  // Eight pages and no "Importar": a statement is imported from its account (015), and the
  // catalogue joined the menu after Comparar (025).
  await expect(nav.getByRole('link')).toHaveText([
    'Início',
    'Lançamentos',
    'Contas',
    'Investimentos',
    'Comparar',
    'Dados de mercado',
    'Categorias',
    'Configurações',
  ]);
  const pages: [string, string, RegExp][] = [
    ['Lançamentos', 'Lançamentos', /\/transactions$/],
    ['Contas', 'Contas', /\/accounts$/],
    ['Investimentos', 'Investimentos', /\/investments$/],
    ['Comparar', 'Comparar', /\/compare$/],
    ['Dados de mercado', 'Dados de mercado', /\/market-data$/],
    ['Categorias', 'Categorias', /\/categories$/],
    ['Configurações', 'Configurações', /\/settings$/],
    ['Início', 'Início', /\/$/],
  ];

  for (const [link, heading, url] of pages) {
    await nav.getByRole('link', { name: link, exact: true }).click();

    await expect(page).toHaveURL(url);
    await expect(page.getByRole('heading', { level: 2, name: heading, exact: true })).toBeVisible();
    await expect(nav.getByRole('link', { name: link, exact: true })).toHaveAttribute('data-status', 'active');
  }

  // Only one link is the current page.
  await expect(nav.locator('a[data-status="active"]')).toHaveCount(1);
});

test('the returns page, outside the sidebar, opens by its address', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-nav-address'), 'Grace Hopper');

  await page.goto('/investments/returns');
  await expect(page.getByRole('heading', { level: 2, name: 'Rentabilidade' })).toBeVisible();
  await page.getByRole('link', { name: '← Investimentos' }).click();
  await expect(page).toHaveURL(/\/investments$/);
});

test('an account or an asset that does not exist says so', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-nav-missing'), 'Alan Turing');
  // With no account at all, /accounts shows its empty state instead of the detail.
  await createAccount(page, 'Conta existente');
  const missing = crypto.randomUUID();

  await page.goto(`/accounts/${missing}`);
  await expect(page.getByText('Conta não encontrada.')).toBeVisible();

  await page.goto(`/investments/${missing}`);
  await expect(page.getByText('Ativo não encontrado.')).toBeVisible();

  await page.goto(`/investments/${missing}/returns`);
  await expect(page.getByText('Ativo não encontrado.')).toBeVisible();
});

test('an address that matches no page answers in Portuguese', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-nav-unknown'), 'Katherine Johnson');
  await page.goto('/nao-existe');

  await expect(page.getByRole('heading', { name: 'Finanças Pessoais' })).toBeVisible();
  await expect(page.locator('main')).not.toContainText('Not Found');
  await expect(page.locator('main')).toContainText(/não encontrada/i);
});
