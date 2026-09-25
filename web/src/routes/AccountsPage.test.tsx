import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import type { SeenRequest } from '@/test-utils';

import { account, batch, renderAt, stubAccountsApi, type AccountsApi } from './accounts-fixtures';

const card = account('acc-card', 'Cartão', { type: 'CreditCard', openingBalance: -1234.56 });

const writes = (seen: SeenRequest[]) => seen.filter((request) => request.method !== 'GET');

/** Intl writes a no-break space after the currency symbol; `\s` matches it. */
const text = (element: HTMLElement) => (element.textContent ?? '').replace(/\s+/g, ' ');

async function createWithOpeningBalance(typed: string | null, answer?: { status: number; body: unknown }) {
  const user = userEvent.setup();
  const state: AccountsApi = { accounts: [] };
  const seen = stubAccountsApi(state, (request) => {
    if (request.method !== 'POST') {
      return undefined;
    }

    if (answer) {
      return answer;
    }

    state.accounts = [card];
    return { status: 201, body: card };
  });

  const router = renderAt('/accounts');

  await user.click(await screen.findByRole('button', { name: 'Nova conta' }));
  await user.type(screen.getByLabelText('Nome'), 'Cartão');

  if (typed !== null) {
    await user.clear(screen.getByLabelText('Saldo inicial'));
    await user.type(screen.getByLabelText('Saldo inicial'), typed);
  }

  await user.click(screen.getByRole('button', { name: 'Criar conta' }));

  return { seen, router };
}

describe('AccountsPage, the list (015)', () => {
  beforeEach(() => {
    // Only the clock: the last-import line drops the year inside the current one.
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date('2026-09-25T12:00:00'));
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  const itau = account('acc-itau', 'Itaú', { openingBalance: 1000 });
  const savings = account('acc-savings', 'Poupança Itaú', { type: 'Savings' });

  /** Spec 015 tests 1 and 2. */
  it('shows each account with its current balance, type and last import, and the total', async () => {
    stubAccountsApi({
      accounts: [itau, savings, card],
      balances: { 'acc-itau': 29229.57, 'acc-savings': 48224.01, 'acc-card': -4848.3 },
      total: 72605.28,
      imports: [
        batch('b-aug', itau, { committedAt: '2026-08-25T15:00:00Z' }),
        batch('b-sep', itau, { committedAt: '2026-09-23T15:00:00Z' }),
        batch('b-open', savings, { status: 'Staged', committedCount: null, committedAt: null }),
      ],
    });
    renderAt('/accounts');

    const first = await screen.findByTestId('account-card-acc-itau');
    expect(first).toHaveTextContent('Conta corrente · Último extrato em 23/09');
    // The balance, not the opening balance GET /api/accounts carries.
    await waitFor(() => expect(within(first).getByTestId('amount')).toHaveTextContent('+29.229,57'));

    const second = screen.getByTestId('account-card-acc-savings');
    expect(second).toHaveTextContent('Poupança · Extrato em revisão');
    expect(within(second).getByTestId('amount')).toHaveTextContent('+48.224,01');

    const third = screen.getByTestId('account-card-acc-card');
    expect(third).toHaveTextContent('Cartão de crédito · Sem importações');
    expect(within(third).getByTestId('amount')).toHaveTextContent('−4.848,30');
    expect(within(third).getByTestId('amount')).toHaveClass('text-destructive');

    expect(text(screen.getByTestId('accounts-total'))).toContain('R$ 72.605,28');
  });

  it('writes the year of a last import from another year', async () => {
    stubAccountsApi({ accounts: [itau], imports: [batch('b-old', itau, { committedAt: '2025-12-20T15:00:00Z' })] });
    renderAt('/accounts');

    expect(await screen.findByTestId('account-card-acc-itau')).toHaveTextContent('Último extrato em 20/12/2025');
  });

  /** Spec 015 test 3. */
  it('opens the first account and marks its card current', async () => {
    stubAccountsApi({ accounts: [itau, savings], balances: { 'acc-itau': 29229.57 } });
    const router = renderAt('/accounts');

    expect(await screen.findByRole('heading', { name: 'Itaú' })).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/accounts/acc-itau');
    expect(screen.getByRole('link', { name: 'Itaú' })).toHaveAttribute('aria-current', 'page');
    expect(screen.getByRole('link', { name: 'Poupança Itaú' })).not.toHaveAttribute('aria-current');
    expect(text(screen.getByTestId('account-balance'))).toContain('R$ +29.229,57');
  });

  it('says so when the account in the address does not exist', async () => {
    stubAccountsApi({ accounts: [itau] });
    renderAt('/accounts/acc-gone');

    expect(await screen.findByText('Conta não encontrada.')).toBeInTheDocument();
  });
});

describe('AccountsPage, creating and editing', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  /** Spec 015 test 4. */
  it('with no accounts, the empty state opens the form, and the new account is selected', async () => {
    const user = userEvent.setup();
    const state: AccountsApi = { accounts: [] };
    stubAccountsApi(state, (request) => {
      if (request.method !== 'POST') {
        return undefined;
      }

      state.accounts = [card];
      return { status: 201, body: card };
    });
    const router = renderAt('/accounts');

    expect(await screen.findByText(/Nenhuma conta ainda/)).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Criar a primeira conta' }));
    await user.type(screen.getByLabelText('Nome'), 'Cartão');
    await user.click(screen.getByRole('button', { name: 'Criar conta' }));

    expect(await screen.findByRole('heading', { name: 'Cartão' })).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/accounts/acc-card');
    expect(screen.queryByRole('button', { name: 'Criar conta' })).not.toBeInTheDocument();
  });

  /** 005 amendment 3. Typed the way it is shown, pt-BR grouping and a sign included. */
  it('sends the opening balance typed in pt-BR, sign kept', async () => {
    const { seen } = await createWithOpeningBalance('-1.234,56');

    await waitFor(() => expect(writes(seen)).toHaveLength(1));
    expect(writes(seen)[0]?.body).toMatchObject({ name: 'Cartão', openingBalance: -1234.56 });
  });

  /** 005 amendment 3: the default is zero. */
  it('sends zero when the opening balance is left alone', async () => {
    const { seen } = await createWithOpeningBalance(null);

    await waitFor(() => expect(writes(seen)).toHaveLength(1));
    expect(writes(seen)[0]?.body).toMatchObject({ openingBalance: 0 });
  });

  it('refuses an opening balance that is not a number, without a round trip', async () => {
    const { seen } = await createWithOpeningBalance('abc');

    expect(await screen.findByText('Informe um número.')).toBeInTheDocument();
    expect(writes(seen)).toHaveLength(0);
  });

  /** A 400 names its field; the message is shown under that field, verbatim. */
  it('shows the API message for the opening balance field', async () => {
    await createWithOpeningBalance('10', {
      status: 400,
      body: {
        title: 'One or more validation errors occurred.',
        errors: { openingBalance: ['Saldo inicial inválido.'] },
      },
    });

    expect(await screen.findByText('Saldo inicial inválido.')).toBeInTheDocument();
    expect(screen.queryByText(/validation errors/)).not.toBeInTheDocument();
  });

  /** Spec 015 test 6. The details tab opens on the stored value and sends it back unchanged. */
  it('keeps the stored opening balance when editing', async () => {
    const user = userEvent.setup();
    const seen = stubAccountsApi({ accounts: [card] }, (request) =>
      request.method === 'PUT' ? { body: card } : undefined,
    );

    renderAt('/accounts/acc-card?tab=details');

    expect(await screen.findByLabelText('Saldo inicial')).toHaveValue('-1234,56');
    await user.click(screen.getByRole('button', { name: 'Salvar conta' }));

    await waitFor(() => expect(writes(seen)).toHaveLength(1));
    expect(writes(seen)[0]).toMatchObject({
      method: 'PUT',
      url: '/api/accounts/acc-card',
      body: { openingBalance: -1234.56 },
    });
    expect(await screen.findByRole('status')).toHaveTextContent('Conta salva.');
  });

  /** Spec 015 test 7. */
  it('shows the sentence of a refused delete', async () => {
    const user = userEvent.setup();
    const sentence = "'Cartão' ainda tem 6 lançamento(s). Mova ou exclua os lançamentos antes de excluir a conta.";
    stubAccountsApi({ accounts: [card] }, (request) =>
      request.method === 'DELETE' ? { status: 409, body: { status: 409, title: 'Conflict', detail: sentence } } : undefined,
    );

    renderAt('/accounts/acc-card?tab=details');

    await user.click(await screen.findByRole('button', { name: 'Excluir conta' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(sentence);
  });

  it('goes back to the list after a delete', async () => {
    const user = userEvent.setup();
    const other = account('acc-other', 'Nubank');
    const state: AccountsApi = { accounts: [other, card] };
    stubAccountsApi(state, (request) => {
      if (request.method !== 'DELETE') {
        return undefined;
      }

      state.accounts = [other];
      return { status: 204 };
    });
    const router = renderAt('/accounts/acc-card?tab=details');

    await user.click(await screen.findByRole('button', { name: 'Excluir conta' }));

    await waitFor(() => expect(router.state.location.pathname).toBe('/accounts/acc-other'));
    expect(screen.queryByRole('link', { name: 'Cartão' })).not.toBeInTheDocument();
  });
});
