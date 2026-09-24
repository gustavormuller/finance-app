import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';

import type { Account } from '@/api/finance';
import { renderWithClient, stubFetch, type SeenRequest } from '@/test-utils';

import AccountsPage from './AccountsPage';

const card: Account = {
  id: 'acc-card',
  name: 'Cartão',
  type: 'CreditCard',
  currency: 'BRL',
  createdAt: '2026-09-01T00:00:00Z',
  openingBalance: -1234.56,
};

const writes = (seen: SeenRequest[]) => seen.filter((request) => request.method !== 'GET');

async function createWithOpeningBalance(typed: string | null, respond?: Parameters<typeof stubFetch>[0]) {
  const user = userEvent.setup();
  const seen = stubFetch(
    respond ?? ((request) => (request.method === 'POST' ? { status: 201, body: card } : { body: [] })),
  );

  renderWithClient(<AccountsPage />);

  await user.click(screen.getByRole('button', { name: 'Nova conta' }));
  await user.type(screen.getByLabelText('Nome'), 'Cartão');

  if (typed !== null) {
    await user.clear(screen.getByLabelText('Saldo inicial'));
    await user.type(screen.getByLabelText('Saldo inicial'), typed);
  }

  await user.click(screen.getByRole('button', { name: 'Criar conta' }));

  return seen;
}

describe('AccountsPage', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  /** 005 amendment 3. Typed the way it is shown, pt-BR grouping and a sign included. */
  it('sends the opening balance typed in pt-BR, sign kept', async () => {
    const seen = await createWithOpeningBalance('-1.234,56');

    await waitFor(() => expect(writes(seen)).toHaveLength(1));
    expect(writes(seen)[0]?.body).toMatchObject({ name: 'Cartão', openingBalance: -1234.56 });
  });

  /** 005 amendment 3: the default is zero. */
  it('sends zero when the opening balance is left alone', async () => {
    const seen = await createWithOpeningBalance(null);

    await waitFor(() => expect(writes(seen)).toHaveLength(1));
    expect(writes(seen)[0]?.body).toMatchObject({ openingBalance: 0 });
  });

  it('refuses an opening balance that is not a number, without a round trip', async () => {
    const seen = await createWithOpeningBalance('abc');

    expect(await screen.findByText('Informe um número.')).toBeInTheDocument();
    expect(writes(seen)).toHaveLength(0);
  });

  /** A 400 names its field; the message is shown under that field, verbatim. */
  it('shows the API message for the opening balance field', async () => {
    await createWithOpeningBalance('10', (request) =>
      request.method === 'POST'
        ? {
            status: 400,
            body: {
              title: 'One or more validation errors occurred.',
              errors: { openingBalance: ['Saldo inicial inválido.'] },
            },
          }
        : { body: [] },
    );

    expect(await screen.findByText('Saldo inicial inválido.')).toBeInTheDocument();
    expect(screen.queryByText(/validation errors/)).not.toBeInTheDocument();
  });

  /** Editing opens on the stored value and sends it back unchanged. */
  it('keeps the stored opening balance when editing', async () => {
    const user = userEvent.setup();
    const seen = stubFetch((request) =>
      request.method === 'PUT' ? { body: card } : { body: [card] },
    );

    renderWithClient(<AccountsPage />);

    await user.click(await screen.findByRole('button', { name: 'Editar' }));
    expect(screen.getByLabelText('Saldo inicial')).toHaveValue('-1234,56');

    await user.click(screen.getByRole('button', { name: 'Salvar conta' }));

    await waitFor(() => expect(writes(seen)).toHaveLength(1));
    expect(writes(seen)[0]).toMatchObject({
      method: 'PUT',
      url: '/api/accounts/acc-card',
      body: { openingBalance: -1234.56 },
    });
  });
});
