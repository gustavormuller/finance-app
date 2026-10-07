import { readFile } from 'node:fs/promises';

import { expect, test } from '@playwright/test';

import { createAccount, createTransaction, devLogin, showTransactionsBetween, uniqueEmail } from './support';

/**
 * Spec 021 E2E test 21, against the real API and a real PostgreSQL. 024: on fixed dates
 * and explicit filters, so it holds in any month, and the file carries the account filter.
 */
test('Exportar CSV downloads the rows the filters select, as Excel pt-BR reads them', async ({ page }) => {
  await devLogin(page, uniqueEmail('e2e-export'), 'Ada Lovelace');
  await createAccount(page, 'Nubank');
  await createAccount(page, 'Inter');

  // Nothing listed, nothing to export.
  await page.goto('/transactions');
  await expect(page.getByRole('button', { name: 'Exportar CSV' })).toBeDisabled();

  await createTransaction(page, {
    account: 'Nubank',
    category: 'Alimentação',
    amount: '1234.56',
    date: '2026-09-01',
    description: 'Mercado; "Pão"',
  });
  await createTransaction(page, { account: 'Inter', category: 'Lazer', amount: '10', date: '2026-09-02', description: 'Cinema no Inter' });
  await createTransaction(page, { account: 'Nubank', category: 'Lazer', amount: '5', date: '2026-08-31', description: 'Fora do período' });

  await showTransactionsBetween(page, '2026-09-01', '2026-09-30');
  await page.getByLabel('Filtrar por conta').selectOption({ label: 'Nubank' });
  await expect(page.getByRole('row', { name: /Mercado/ })).toBeVisible();
  await expect(page.getByRole('row', { name: /Cinema no Inter/ })).toHaveCount(0);

  const downloading = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Exportar CSV' }).click();
  const download = await downloading;

  expect(download.suggestedFilename()).toBe('lancamentos-2026-09-01-a-2026-09-30.csv');

  const file = await readFile(await download.path());

  expect([...file.subarray(0, 3)]).toEqual([0xef, 0xbb, 0xbf]);
  expect(file.subarray(3).toString('utf8')).toBe(
    'Data;Descrição;Valor;Moeda;Conta;Categoria;Subcategoria\r\n' +
      '01/09/2026;"Mercado; ""Pão""";-1234,56;BRL;Nubank;Alimentação;\r\n',
  );
});
