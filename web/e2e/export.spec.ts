import { readFile } from 'node:fs/promises';

import { expect, test } from '@playwright/test';

import { createAccount, createTransaction, devLogin, uniqueEmail } from './support';

/** Spec 021 E2E test 21, against the real API and a real PostgreSQL. */
test('Exportar CSV downloads the month the list shows, as Excel pt-BR reads it', async ({ page }) => {
  // The list opens on the current month, so the transaction goes on its first day.
  const now = new Date();
  const year = String(now.getFullYear());
  const month = String(now.getMonth() + 1).padStart(2, '0');
  const lastDay = String(new Date(now.getFullYear(), now.getMonth() + 1, 0).getDate());

  await devLogin(page, uniqueEmail('e2e-export'), 'Ada Lovelace');
  await createAccount(page, 'Nubank');
  await createTransaction(page, {
    account: 'Nubank',
    category: 'Alimentação',
    amount: '1234.56',
    date: `${year}-${month}-01`,
    description: 'Mercado; "Pão"',
  });

  await page.goto('/transactions');
  await expect(page.getByRole('row', { name: /Mercado/ })).toBeVisible();

  const downloading = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Exportar CSV' }).click();
  const download = await downloading;

  expect(download.suggestedFilename()).toBe(`lancamentos-${year}-${month}-01-a-${year}-${month}-${lastDay}.csv`);

  const file = await readFile(await download.path());

  expect([...file.subarray(0, 3)]).toEqual([0xef, 0xbb, 0xbf]);
  expect(file.subarray(3).toString('utf8')).toBe(
    'Data;Descrição;Valor;Moeda;Conta;Categoria;Subcategoria\r\n' +
      `01/${month}/${year};"Mercado; ""Pão""";-1234,56;BRL;Nubank;Alimentação;\r\n`,
  );
});
