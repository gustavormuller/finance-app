import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import type { Transaction } from '@/api/finance';
import type { SeenRequest } from '@/test-utils';

import { account, renderAt, stubAccountsApi } from './accounts-fixtures';

const nubank = account('acc-nubank', 'Nubank');

const groceries: Transaction = {
  id: 't1',
  accountId: nubank.id,
  accountName: nubank.name,
  categoryId: 'cat-food',
  categoryName: 'Alimentação',
  amount: -1234.56,
  currency: 'BRL',
  date: '2026-09-13',
  description: 'Mercado',
  createdAt: '2026-09-13T12:00:00Z',
};

const CSV = '﻿Data;Descrição;Valor;Moeda;Conta;Categoria;Subcategoria\r\n13/09/2026;Mercado;-1234,56;BRL;Nubank;Alimentação;\r\n';

const FILE = {
  text: CSV,
  headers: {
    'Content-Type': 'text/csv; charset=utf-8',
    'Content-Disposition':
      "attachment; filename=lancamentos-2026-09-01-a-2026-09-30.csv; filename*=UTF-8''lancamentos-2026-09-01-a-2026-09-30.csv",
  },
};

/** What the page asked the export for, path and query, in order. */
const exports = (seen: SeenRequest[]) =>
  seen.map((request) => request.url).filter((url) => url.startsWith('/api/transactions/export'));

describe('TransactionsPage, the export (021)', () => {
  let saved: { name: string; blob: Blob }[];

  beforeEach(() => {
    // Only the clock: the filter opens on the current month.
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date('2026-09-25T12:00:00'));

    saved = [];
    const blobs: Blob[] = [];
    Object.assign(URL, {
      createObjectURL: (blob: Blob) => `blob:${blobs.push(blob)}`,
      revokeObjectURL: () => {},
    });
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
      saved.push({ name: this.download, blob: blobs[Number(this.href.slice('blob:'.length)) - 1]! });
    });
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  /** Spec 021 web test 16. */
  it('exports what the filter bar selects and saves it under the name the server gives', async () => {
    const user = userEvent.setup();
    const seen = stubAccountsApi({ accounts: [nubank], transactions: [groceries] }, (_, url) =>
      url.pathname === '/api/transactions/export' ? FILE : undefined,
    );
    renderAt('/transactions');

    await screen.findByRole('row', { name: /Mercado/ });
    await user.click(screen.getByRole('button', { name: 'Exportar CSV' }));

    await waitFor(() => expect(saved).toHaveLength(1));
    expect(exports(seen)).toEqual(['/api/transactions/export?from=2026-09-01&to=2026-09-30']);
    expect(saved[0]!.name).toBe('lancamentos-2026-09-01-a-2026-09-30.csv');
    // As bytes, so the BOM is seen to survive; arrays, because jsdom and Node each have a Uint8Array.
    expect([...new Uint8Array(await saved[0]!.blob.arrayBuffer())]).toEqual([...new TextEncoder().encode(CSV)]);

    await user.selectOptions(screen.getByLabelText('Filtrar por conta'), 'acc-nubank');
    await user.click(screen.getByRole('button', { name: 'Exportar CSV' }));

    await waitFor(() => expect(saved).toHaveLength(2));
    expect(exports(seen)[1]).toBe('/api/transactions/export?from=2026-09-01&to=2026-09-30&accountId=acc-nubank');
  });

  /** Spec 021 web test 17. */
  it('carries the import batch the page was opened with, and no dates', async () => {
    const user = userEvent.setup();
    const seen = stubAccountsApi({ accounts: [nubank], transactions: [groceries] }, (_, url) =>
      url.pathname === '/api/transactions/export' ? FILE : undefined,
    );
    renderAt('/transactions?importBatchId=b1');

    await screen.findByRole('row', { name: /Mercado/ });
    await user.click(screen.getByRole('button', { name: 'Exportar CSV' }));

    await waitFor(() => expect(saved).toHaveLength(1));
    expect(exports(seen)).toEqual(['/api/transactions/export?importBatchId=b1']);
  });

  /** Spec 021 web test 18. */
  it('shows a refusal in pt-BR and saves nothing', async () => {
    const user = userEvent.setup();
    stubAccountsApi({ accounts: [nubank], transactions: [groceries] }, (_, url) =>
      url.pathname === '/api/transactions/export'
        ? {
            status: 400,
            body: {
              title: 'One or more validation errors occurred.',
              errors: { accountId: ['A conta do filtro não é um identificador válido.'] },
            },
          }
        : undefined,
    );
    renderAt('/transactions');

    await screen.findByRole('row', { name: /Mercado/ });
    await user.click(screen.getByRole('button', { name: 'Exportar CSV' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('A conta do filtro não é um identificador válido.');
    expect(saved).toEqual([]);
    expect(screen.getByRole('button', { name: 'Exportar CSV' })).toBeEnabled();
  });

  /** Spec 021 web test 19. */
  it('cannot export while the list shows nothing', async () => {
    stubAccountsApi({ accounts: [nubank], transactions: [] });
    renderAt('/transactions');

    expect(await screen.findByText(/nenhum lançamento/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Exportar CSV' })).toBeDisabled();
  });
});
