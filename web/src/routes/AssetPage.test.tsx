import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { RouterProvider, createMemoryHistory, createRouter } from '@tanstack/react-router';
import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';

import type { DailyRow, Movement, Position, UsdBrl } from '@/api/finance';
import { chooseCurrency } from '@/lib/currency';
import { routeTree } from '@/routeTree';
import '@/test-routes';
import { stubFetch, type SeenRequest } from '@/test-utils';

const me = { id: 'u1', email: 'ada@example.com', displayName: 'Ada Lovelace', aiEnabled: false };

/** Intl writes a no-break space after the currency symbol; `\s` matches it. */
const text = (element: HTMLElement) => (element.textContent ?? '').replace(/\s+/g, ' ');

const petr4: Position = {
  assetId: 'a-petr4',
  ticker: 'PETR4',
  name: 'Petrobras PN',
  class: 'StockBr',
  currency: 'BRL',
  nickname: null,
  quantity: 100,
  averageCost: 32.12,
  price: 35.1,
  priceDate: '2026-09-23',
  valueBrl: 3510,
  costBasisBrl: 3212,
  unrealisedBrl: 298,
  unrealisedPct: 0.0928,
  realisedBrl: 0,
  dividendsBrl: 120,
};

const buy: Movement = {
  id: 'm-buy',
  assetId: 'a-petr4',
  date: '2026-09-01',
  kind: 'Buy',
  quantity: 100,
  unitPrice: 32.12,
  amount: 0,
  fees: 0,
  currency: 'BRL',
  notes: null,
  createdAt: '2026-09-01T12:00:00Z',
};

const dividend: Movement = { ...buy, id: 'm-div', date: '2026-09-15', kind: 'Dividend', quantity: 0, unitPrice: 0, amount: 120, notes: 'Setembro' };

const daily: DailyRow[] = [
  { date: '2026-09-01', quantity: 100, averageCost: 32.12, price: 32.12, priceDate: '2026-09-01', fxRate: 1, valueBrl: 3212, costBasisBrl: 3212 },
  { date: '2026-09-24', quantity: 100, averageCost: 32.12, price: 35.1, priceDate: '2026-09-23', fxRate: 1, valueBrl: 3510, costBasisBrl: 3212 },
];

type Answer = { status?: number; body?: unknown } | undefined;

function stubAsset(
  state: { movements: Movement[]; daily?: DailyRow[]; usdBrl?: UsdBrl },
  write: (request: SeenRequest) => Answer = () => undefined,
) {
  return stubFetch((request) => {
    const url = new URL(request.url, 'http://localhost');

    if (request.method !== 'GET') {
      return write(request);
    }

    switch (url.pathname) {
      case '/api/auth/me':
        return { body: me };
      case '/api/investments/assets':
        return { body: [petr4] };
      case '/api/investments/summary':
        return {
          body: {
            totalBrl: 3510,
            totalCostBrl: 3212,
            unrealisedBrl: 298,
            allocation: [{ class: 'StockBr', valueBrl: 3510, share: 1 }],
            usdBrl: state.usdBrl ?? null,
          },
        };
      case '/api/market-data/assets':
        return { body: [] };
      case '/api/investments/assets/a-petr4/movements':
        return { body: state.movements };
      case '/api/investments/assets/a-petr4/daily':
        return { body: state.daily ?? daily };
      default:
        return undefined;
    }
  });
}

function renderAt(path: string) {
  const router = createRouter({ routeTree, history: createMemoryHistory({ initialEntries: [path] }) });
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  render(
    <QueryClientProvider client={client}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  );

  return router;
}

describe('AssetPage', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    act(() => chooseCurrency('BRL'));
    localStorage.clear();
  });

  /** Spec 016 web test 13. */
  it('shows its money in dollars at the latest rate, its prices in its own currency, and its history in reais', async () => {
    localStorage.setItem('currency', 'USD');
    stubAsset({ movements: [buy, dividend], usdBrl: { rate: 5, date: '2026-09-23' } });
    renderAt('/investments/a-petr4');

    const summary = await screen.findByTestId('asset-summary');
    await waitFor(() => expect(text(summary)).toContain('US$ 702,00'));
    expect(text(summary)).toContain('+US$ 59,60 (+9,28%)');
    expect(text(summary)).toContain('US$ 24,00');
    expect(text(summary)).toContain('R$ 32,12');
    expect(text(screen.getByTestId('currency-note'))).toBe('em dólar · US$ 1 = R$ 5,00 em 23/09');
    expect(within(screen.getByRole('group', { name: 'Moeda' })).getByRole('button', { name: 'US$' })).toHaveAttribute('aria-pressed', 'true');
    expect(screen.getByRole('heading', { name: 'Valor ao longo do tempo, em reais' })).toBeInTheDocument();
    // A movement is what was paid, in the asset's currency, whatever the page shows.
    expect(text(screen.getByTestId('movement-m-buy'))).toContain('R$ 32,12');
  });

  it('shows the position and its movements, each kind in Portuguese', async () => {
    stubAsset({ movements: [buy, dividend] });
    renderAt('/investments/a-petr4');

    expect(await screen.findByRole('heading', { name: 'PETR4' })).toBeInTheDocument();
    const summary = await screen.findByTestId('asset-summary');
    expect(text(summary)).toContain('R$ 3.510,00');
    expect(within(summary).getByText('Proventos')).toBeInTheDocument();
    expect(text(summary)).toContain('R$ 120,00');

    const bought = await screen.findByTestId('movement-m-buy');
    expect(bought).toHaveTextContent('01/09/2026');
    expect(bought).toHaveTextContent('Compra');
    expect(text(bought)).toContain('R$ 32,12');
    const paid = screen.getByTestId('movement-m-div');
    expect(paid).toHaveTextContent('Dividendo');
    expect(text(paid)).toContain('R$ 120,00');
    expect(paid).toHaveTextContent('Setembro');

    expect(screen.getByTestId('value-chart')).toBeInTheDocument();
  });

  it('records a movement and shows a 400 under the field it names', async () => {
    const state = { movements: [buy] };
    let answer: Answer = { status: 400, body: { title: 'Bad Request', errors: { quantity: ['Quantidade vendida maior que a posição'] } } };
    const seen = stubAsset(state, () => answer);
    renderAt('/investments/a-petr4');

    await userEvent.click(await screen.findByRole('button', { name: 'Nova movimentação' }));
    const form = screen.getByRole('form', { name: 'Movimentação' });
    await userEvent.selectOptions(within(form).getByLabelText('Tipo'), 'Sell');
    await userEvent.type(within(form).getByLabelText('Quantidade'), '500');
    await userEvent.type(within(form).getByLabelText('Preço unitário'), '40');
    await userEvent.click(within(form).getByRole('button', { name: 'Registrar movimentação' }));

    const quantity = within(form).getByLabelText('Quantidade').closest('div')!;
    expect(await within(quantity).findByText('Quantidade vendida maior que a posição')).toBeInTheDocument();

    const sold: Movement = { ...buy, id: 'm-sell', kind: 'Sell', quantity: 50, unitPrice: 40, date: '2026-09-24' };
    answer = { status: 201, body: sold };
    await userEvent.clear(within(form).getByLabelText('Quantidade'));
    await userEvent.type(within(form).getByLabelText('Quantidade'), '50');
    state.movements = [buy, sold];
    await userEvent.click(within(form).getByRole('button', { name: 'Registrar movimentação' }));

    expect(await screen.findByTestId('movement-m-sell')).toHaveTextContent('Venda');
    expect(screen.queryByRole('form', { name: 'Movimentação' })).not.toBeInTheDocument();
    const post = seen.filter((request) => request.method === 'POST').at(-1)!;
    expect(post.url).toBe('/api/investments/assets/a-petr4/movements');
    expect(post.body).toMatchObject({ kind: 'Sell', quantity: 50, unitPrice: 40 });
  });

  it('edits a movement in place', async () => {
    const seen = stubAsset({ movements: [buy] }, (request) => (request.method === 'PUT' ? { body: { ...buy, quantity: 120 } } : undefined));
    renderAt('/investments/a-petr4');

    await userEvent.click(within(await screen.findByTestId('movement-m-buy')).getByRole('button', { name: 'Editar' }));
    const form = screen.getByRole('form', { name: 'Movimentação' });
    expect(within(form).getByLabelText('Quantidade')).toHaveValue('100');
    await userEvent.clear(within(form).getByLabelText('Quantidade'));
    await userEvent.type(within(form).getByLabelText('Quantidade'), '120');
    await userEvent.click(within(form).getByRole('button', { name: 'Salvar movimentação' }));

    await waitFor(() => expect(seen.some((request) => request.method === 'PUT')).toBe(true));
    const put = seen.find((request) => request.method === 'PUT')!;
    expect(put.url).toBe('/api/investments/movements/m-buy');
    expect(put.body).toMatchObject({ kind: 'Buy', quantity: 120, unitPrice: 32.12, date: '2026-09-01' });
  });

  it('shows the refusal of a delete verbatim', async () => {
    const detail = 'Quantidade vendida maior que a posição';
    const seen = stubAsset({ movements: [buy] }, () => ({ status: 409, body: { title: 'Conflict', detail } }));
    renderAt('/investments/a-petr4');

    await userEvent.click(within(await screen.findByTestId('movement-m-buy')).getByRole('button', { name: 'Excluir' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(detail);
    expect(seen.find((request) => request.method === 'DELETE')?.url).toBe('/api/investments/movements/m-buy');
  });

  it('says so while there is no value history, and removes an asset with nothing recorded', async () => {
    const seen = stubAsset({ movements: [], daily: [] }, (request) => (request.method === 'DELETE' ? { status: 204 } : undefined));
    const router = renderAt('/investments/a-petr4');

    expect(await screen.findByText('Nenhuma movimentação registrada.')).toBeInTheDocument();
    expect(screen.getByText('Sem histórico de valor ainda.')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Remover ativo' }));

    await waitFor(() => expect(router.state.location.pathname).toBe('/investments'));
    expect(seen.find((request) => request.method === 'DELETE')?.url).toBe('/api/investments/assets/a-petr4');
  });

  it('says so when the asset is not the user’s, or does not exist', async () => {
    stubFetch((request) => {
      const url = new URL(request.url, 'http://localhost');
      if (url.pathname === '/api/auth/me') return { body: me };
      if (url.pathname === '/api/investments/assets') return { body: [] };
      return { status: 404, body: { title: 'Not Found' } };
    });
    renderAt('/investments/someone-else');

    expect(await screen.findByText('Ativo não encontrado.')).toBeInTheDocument();
  });
});
