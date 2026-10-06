import { expect, test } from '@playwright/test';

import { createAccount, devLogin, uniqueEmail } from './support';

/**
 * Spec 024: the shell around every page. The sidebar's links, the pages reached only by
 * their address, and what an address that names nothing shows.
 */

test('the sidebar opens every page and marks the one shown', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-nav'), 'Ada Lovelace');
  await page.goto('/');

  const nav = page.getByRole('navigation', { name: 'Principal' });
  const pages: [string, string, RegExp][] = [
    ['Lançamentos', 'Lançamentos', /\/transactions$/],
    ['Contas', 'Contas', /\/accounts$/],
    ['Investimentos', 'Investimentos', /\/investments$/],
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

test('the pages outside the sidebar open by their address', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-nav-address'), 'Grace Hopper');

  await page.goto('/market-data');
  await expect(page.getByRole('heading', { level: 2, name: 'Dados de mercado' })).toBeVisible();

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
  test.fixme(true, 'Bug: the router has no notFoundComponent, so TanStack Router renders its English "Not Found".');

  await devLogin(page, uniqueEmail('e2e-nav-unknown'), 'Katherine Johnson');
  await page.goto('/nao-existe');

  await expect(page.getByRole('heading', { name: 'Finanças Pessoais' })).toBeVisible();
  await expect(page.locator('main')).not.toContainText('Not Found');
  await expect(page.locator('main')).toContainText(/não encontrada/i);
});
