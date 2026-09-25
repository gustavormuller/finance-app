import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { RouterProvider, createMemoryHistory, createRouter } from '@tanstack/react-router';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import type { MarketAsset, PortfolioSummary, Position } from '@/api/finance';
import { routeTree } from '@/routeTree';
import { stubFetch, type SeenRequest } from '@/test-utils';

const me = { id: 'u1', email: 'ada@example.com', displayName: 'Ada Lovelace', aiEnabled: false };

const plain = (element: HTMLElement) => (element.textContent ?? '').replace(/\u00a0/g, ' ');

const empty = {
  price: null,
  priceDate: null,
  valueBrl: null,
  costBasisBrl: null,
  unrealisedBrl: null,
  unrealisedPct: null,
  realisedBrl: 0,
  dividendsBrl: 0,
};

const petr4: Position = {
  assetId: 'a-petr4',
  ticker: 'PETR4',
  name: 'Petrobras PN',
  class: 'StockBr',
  currency: 'BRL',
  nickname: null,
  quantity: 100,
  averageCost: 32.1234,
  price: 35.1,
  priceDate: '2026-09-23',
  valueBrl: 3510,
  costBasisBrl: 3212.34,
  unrealisedBrl: 297.66,
  unrealisedPct: 0.0927,
  realisedBrl: 0,
  dividendsBrl: 120,
};

// Friday the 17th, read on Thursday the 24th: five business days old.
const aapl: Position = {
  ...petr4,
  assetId: 'a-aapl',
  ticker: 'AAPL',
  name: 'Apple Inc.',
  class: 'StockUs',
  currency: 'USD',
  quantity: 10,
  averageCost: 150,
  price: 200,
  priceDate: '2026-09-17',
  valueBrl: 11000,
  costBasisBrl: 8000,
  unrealisedBrl: 3000,
  unrealisedPct: 0.375,
  dividendsBrl: 0,
};

const soldOut: Position = { ...petr4, assetId: 'a-vale3', ticker: 'VALE3', name: 'Vale ON', quantity: 0, averageCost: 0, valueBrl: 0, costBasisBrl: 0, unrealisedBrl: 0, unrealisedPct: null, realisedBrl: 500 };

const justAdded: Position = { ...petr4, ...empty, assetId: 'a-itub4', ticker: 'ITUB4', name: 'Itaú PN', quantity: 0, averageCost: 0 };

const summary: PortfolioSummary = {
  totalBrl: 14510,
  totalCostBrl: 11212.34,
  unrealisedBrl: 3297.66,
  allocation: [
    { class: 'StockUs', valueBrl: 11000, share: 0.7581 },
    { class: 'StockBr', valueBrl: 3510, share: 0.2419 },
  ],
  usdBrl: { rate: 5.5, date: '2026-09-23' },
};

const nothing: PortfolioSummary = { totalBrl: 0, totalCostBrl: 0, unrealisedBrl: 0, allocation: [], usdBrl: null };

function stubInvestments(positions: () => Position[], extra: (request: SeenRequest, url: URL) => { status?: number; body?: unknown } | undefined = () => undefined) {
  return stubFetch((request) => {
    const url = new URL(request.url, 'http://localhost');
    const answer = extra(request, url);
    if (answer) {
      return answer;
    }

    // The detail page a successful add opens: nothing recorded yet.
    if (request.method === 'GET' && /^\/api\/investments\/assets\/[^/]+\/(movements|daily)$/.test(url.pathname)) {
      return { body: [] };
    }

    switch (`${request.method} ${url.pathname}`) {
      case 'GET /api/auth/me':
        return { body: me };
      case 'GET /api/investments/assets':
        return { body: positions() };
      case 'GET /api/investments/summary':
        return { body: positions().length === 0 ? nothing : summary };
      case 'GET /api/market-data/assets':
        return { body: [] };
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

describe('InvestmentsPage: positions', () => {
  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date(2026, 8, 24, 10, 0));
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  it('is in the navigation', async () => {
    stubInvestments(() => []);
    renderAt('/');

    const link = await screen.findByRole('link', { name: 'Investimentos' });
    expect(link).toHaveAttribute('href', '/investments');
  });

  it('lists each open position with its figures in pt-BR, and the total', async () => {
    stubInvestments(() => [aapl, petr4, soldOut, justAdded]);
    renderAt('/investments');

    const row = await screen.findByTestId('position-a-petr4');
    expect(within(row).getByRole('link', { name: 'PETR4' })).toHaveAttribute('href', '/investments/a-petr4');
    expect(row).toHaveTextContent('Petrobras PN');
    expect(plain(row)).toContain('100');
    expect(plain(row)).toContain('R$ 32,1234');
    expect(plain(row)).toContain('R$ 35,10');
    expect(row).toHaveTextContent('23/09/2026');
    expect(plain(row)).toContain('R$ 3.510,00');
    expect(plain(row)).toContain('+R$ 297,66');
    expect(plain(row)).toContain('+9,27%');

    // A USD asset: its price in dollars, its value in reais.
    const usd = screen.getByTestId('position-a-aapl');
    expect(plain(usd)).toContain('US$ 200,00');
    expect(plain(usd)).toContain('R$ 11.000,00');

    // In the API's order, by value.
    expect(screen.getAllByTestId(/^position-a-/).map((element) => element.dataset.testid)).toEqual(['position-a-aapl', 'position-a-petr4']);

    const total = screen.getByTestId('positions-total');
    expect(plain(total)).toContain('R$ 14.510,00');
    expect(plain(total)).toContain('+R$ 3.297,66');
  });

  /** Spec web unit test 30. */
  it('flags a price older than three business days', async () => {
    stubInvestments(() => [aapl, petr4]);
    renderAt('/investments');

    const stale = within(await screen.findByTestId('position-a-aapl')).getByTestId('stale-price');
    expect(stale).toHaveTextContent('Cotação desatualizada');
    expect(within(screen.getByTestId('position-a-petr4')).queryByTestId('stale-price')).not.toBeInTheDocument();
  });

  it('hides positions at zero until asked, then shows them without a valuation where there is none', async () => {
    stubInvestments(() => [petr4, soldOut, justAdded]);
    renderAt('/investments');

    await screen.findByTestId('position-a-petr4');
    expect(screen.queryByTestId('position-a-vale3')).not.toBeInTheDocument();
    expect(screen.queryByTestId('position-a-itub4')).not.toBeInTheDocument();

    await userEvent.click(screen.getByLabelText('Mostrar ativos sem posição (2)'));

    expect(screen.getByTestId('position-a-vale3')).toBeInTheDocument();
    expect(screen.getByTestId('position-a-itub4')).toHaveTextContent('Sem cotação');
  });

  it('says so when nothing is held', async () => {
    stubInvestments(() => []);
    renderAt('/investments');

    expect(await screen.findByText('Nenhum ativo na carteira ainda.')).toBeInTheDocument();
  });

  it('tells every position closed apart from an empty portfolio', async () => {
    stubInvestments(() => [soldOut]);
    renderAt('/investments');

    expect(await screen.findByText('Nenhuma posição em aberto.')).toBeInTheDocument();
  });
});

const petr4Market: MarketAsset = {
  id: 'm-petr4',
  ticker: 'PETR4',
  name: 'Petrobras PN',
  class: 'StockBr',
  currency: 'BRL',
  provider: 'Brapi',
  providerSymbol: 'PETR4',
  isActive: true,
  lastSyncedAt: null,
  createdAt: '2026-09-01T12:00:00Z',
};

const added: Position = { ...justAdded, assetId: 'a-petr4', ticker: 'PETR4', name: 'Petrobras PN' };

describe('InvestmentsPage: adding an asset', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('adds an asset found in the catalogue and opens it', async () => {
    let positions: Position[] = [];
    const seen = stubInvestments(
      () => positions,
      (request, url) => {
        if (request.method === 'GET' && url.pathname === '/api/market-data/assets' && url.searchParams.get('q') === 'petr') {
          return { body: [petr4Market] };
        }
        if (request.method === 'POST' && url.pathname === '/api/investments/assets') {
          positions = [added];
          return { status: 201, body: added };
        }
        return undefined;
      },
    );
    const router = renderAt('/investments');

    await userEvent.type(await screen.findByLabelText('Buscar no catálogo'), 'petr');
    await userEvent.click(screen.getByRole('button', { name: 'Buscar' }));
    const result = await screen.findByTestId('catalogue-result-m-petr4');
    expect(result).toHaveTextContent('Petrobras PN');
    expect(result).toHaveTextContent('Ação (B3)');

    await userEvent.click(within(result).getByRole('button', { name: 'Adicionar' }));

    expect(await screen.findByRole('heading', { name: 'PETR4' })).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/investments/a-petr4');
    expect(seen.find((request) => request.method === 'POST')?.body).toEqual({ marketAssetId: 'm-petr4' });
  });

  it('says when the search finds nothing', async () => {
    stubInvestments(() => []);
    renderAt('/investments');

    await userEvent.type(await screen.findByLabelText('Buscar no catálogo'), 'xyz');
    await userEvent.click(screen.getByRole('button', { name: 'Buscar' }));

    expect(await screen.findByText('Nenhum ativo corresponde a esta busca. Cadastre-o abaixo.')).toBeInTheDocument();
  });

  it('registers a ticker missing from the catalogue and adds it in one step', async () => {
    let positions: Position[] = [];
    const seen = stubInvestments(
      () => positions,
      (request, url) => {
        if (request.method === 'POST' && url.pathname === '/api/investments/assets') {
          positions = [added];
          return { status: 201, body: added };
        }
        return undefined;
      },
    );
    const router = renderAt('/investments');

    await userEvent.click(await screen.findByRole('button', { name: 'Cadastrar novo ativo' }));
    const form = screen.getByRole('form', { name: 'Cadastrar e adicionar ativo' });
    await userEvent.type(within(form).getByLabelText('Ticker'), 'PETR4');
    await userEvent.selectOptions(within(form).getByLabelText('Classe'), 'StockBr');
    await userEvent.selectOptions(within(form).getByLabelText('Provedor'), 'Brapi');
    await userEvent.type(within(form).getByLabelText('Símbolo no provedor'), 'PETR4');
    await userEvent.selectOptions(within(form).getByLabelText('Moeda'), 'BRL');
    await userEvent.click(within(form).getByRole('button', { name: 'Cadastrar e adicionar' }));

    expect(await screen.findByRole('heading', { name: 'PETR4' })).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/investments/a-petr4');
    expect(seen.find((request) => request.method === 'POST')?.body).toEqual({
      ticker: 'PETR4',
      name: '',
      class: 'StockBr',
      provider: 'Brapi',
      providerSymbol: 'PETR4',
      currency: 'BRL',
    });
  });

  it('shows a 400 under the field it names and a 409 verbatim', async () => {
    const currency = 'Ativos do CoinGecko são cotados em USD.';
    const held = 'Você já possui este ativo na carteira.';
    let answer: { status: number; body: unknown } = {
      status: 400,
      body: { title: 'One or more validation errors occurred.', errors: { currency: [currency] } },
    };
    stubInvestments(
      () => [petr4],
      (request, url) => {
        if (request.method === 'GET' && url.pathname === '/api/market-data/assets' && url.searchParams.get('q') === 'petr') {
          return { body: [petr4Market] };
        }
        return request.method === 'POST' && url.pathname === '/api/investments/assets' ? answer : undefined;
      },
    );
    renderAt('/investments');

    await userEvent.click(await screen.findByRole('button', { name: 'Cadastrar novo ativo' }));
    const form = screen.getByRole('form', { name: 'Cadastrar e adicionar ativo' });
    await userEvent.type(within(form).getByLabelText('Ticker'), 'BTC');
    await userEvent.selectOptions(within(form).getByLabelText('Provedor'), 'CoinGecko');
    await userEvent.type(within(form).getByLabelText('Símbolo no provedor'), 'bitcoin');
    await userEvent.click(within(form).getByRole('button', { name: 'Cadastrar e adicionar' }));

    const currencyField = within(form).getByLabelText('Moeda').closest('div')!;
    expect(await within(currencyField).findByText(currency)).toBeInTheDocument();
    expect(screen.queryByText('One or more validation errors occurred.')).not.toBeInTheDocument();

    answer = { status: 409, body: { title: 'Conflict', detail: held } };
    await userEvent.type(screen.getByLabelText('Buscar no catálogo'), 'petr');
    await userEvent.click(screen.getByRole('button', { name: 'Buscar' }));
    await userEvent.click(within(await screen.findByTestId('catalogue-result-m-petr4')).getByRole('button', { name: 'Adicionar' }));

    expect(await screen.findByText(held)).toBeInTheDocument();
  });
});
