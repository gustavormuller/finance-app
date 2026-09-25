import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { RouterProvider, createMemoryHistory, createRouter } from '@tanstack/react-router';
import { act, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import type { MarketAsset, PortfolioSummary, Position, Returns } from '@/api/finance';
import { chooseCurrency } from '@/lib/currency';
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

/** Since inception, five years: P1's figures. The dollar ends 1% up on its base day. */
const sinceInception: Returns = {
  period: { from: '2021-10-04', to: '2026-09-23', days: 1816 },
  twr: { total: 0.8423, annualised: 0.1309 },
  xirr: 0.1414,
  timingEffect: 0.0105,
  benchmarks: {
    CDI: { total: 0.8441, annualised: 0.1312 },
    IPCA6: { total: 0.6689, annualised: 0.1084 },
    IVVB11: { total: 0.4697, annualised: 0.0805 },
    SELIC: { total: 0.8452, annualised: 0.1314 },
    USDBRL: { total: 0.01, annualised: 0.002 },
  },
  series: [
    { date: '2021-10-03', portfolio: 100, CDI: 100, IPCA6: 100, IVVB11: 100, SELIC: 100, USDBRL: 100 },
    { date: '2024-01-01', portfolio: 150, CDI: 140, IPCA6: 130, IVVB11: 120, SELIC: 140, USDBRL: 90 },
    { date: '2026-09-23', portfolio: 184.23, CDI: 184.41, IPCA6: 166.89, IVVB11: 146.97, SELIC: 184.52, USDBRL: 101 },
  ],
};

/** Year to date: a loss, less than a year, and no dollar that could anchor. */
const thisYear: Returns = {
  period: { from: '2026-01-01', to: '2026-09-23', days: 266 },
  twr: { total: -0.0363, annualised: -0.0493 },
  xirr: -0.041,
  timingEffect: 0.0083,
  benchmarks: { CDI: { total: 0.1052, annualised: 0.1471 }, IPCA6: null, IVVB11: { total: 0.0719, annualised: 0.0999 }, SELIC: null, USDBRL: null },
  series: [
    { date: '2025-12-31', portfolio: 100, CDI: 100, IVVB11: 100 },
    { date: '2026-09-23', portfolio: 96.37, CDI: 110.52, IVVB11: 107.19 },
  ],
};

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
      case 'GET /api/returns/portfolio':
        return { body: url.searchParams.get('period') === 'ytd' ? thisYear : sinceInception };
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

const returnsCalls = (seen: SeenRequest[]) =>
  seen.filter((request) => request.url.startsWith('/api/returns/portfolio')).map((request) => request.url);

describe('InvestmentsPage: returns first (016)', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  /** Spec 016 web test 7. */
  it('leads with the return since inception, its annual rate and the XIRR, above the positions', async () => {
    stubInvestments(() => [aapl, petr4]);
    renderAt('/investments');

    const hero = await screen.findByTestId('returns-hero');
    expect(await within(hero).findByRole('heading', { name: 'Rentabilidade da carteira · desde 04/10/2021' })).toBeInTheDocument();
    const twr = within(hero).getByTestId('hero-twr');
    expect(plain(twr)).toBe('+84,23%');
    expect(twr).toHaveClass('text-positive');
    expect(plain(within(hero).getByTestId('hero-annualised'))).toBe('+13,09% a.a.');
    expect(plain(within(hero).getByTestId('hero-xirr'))).toBe('Retorno do seu dinheiro (considerando quando você aportou): +14,14% a.a.');
    expect(within(hero).getByTestId('comparison-chart')).toBeInTheDocument();

    const positions = await screen.findByTestId('position-a-petr4');
    expect(hero.compareDocumentPosition(positions) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  /** Spec 016 web test 7, the tiles. */
  it('compares with CDI, IPCA + 6% and the S&P 500, the difference in points in its tone', async () => {
    stubInvestments(() => [petr4]);
    renderAt('/investments');

    const cdi = await screen.findByTestId('hero-benchmark-CDI');
    expect(plain(cdi)).toContain('vs CDI +84,41%');
    expect(within(cdi).getByText('-0,18 p.p.')).toHaveClass('text-negative');
    const ipca = screen.getByTestId('hero-benchmark-IPCA6');
    expect(plain(ipca)).toContain('vs IPCA + 6% +66,89%');
    expect(within(ipca).getByText('+17,34 p.p.')).toHaveClass('text-positive');
    const sp500 = screen.getByTestId('hero-benchmark-IVVB11');
    expect(plain(sp500)).toContain('vs S&P 500 +46,97%');
    expect(within(sp500).getByText('+37,26 p.p.')).toHaveClass('text-positive');
    expect(screen.queryByTestId('hero-benchmark-SELIC')).not.toBeInTheDocument();
    expect(screen.queryByTestId('hero-benchmark-USDBRL')).not.toBeInTheDocument();
  });

  /** Spec 016 web test 7, the periods. */
  it('starts at Desde o início, refetches for another period, and has no annual rate under a year', async () => {
    const seen = stubInvestments(() => [petr4]);
    renderAt('/investments');

    const periods = await screen.findByRole('group', { name: 'Período' });
    expect(within(periods).getAllByRole('button').map((button) => button.textContent)).toEqual(['No ano', '12 meses', 'Desde o início']);
    expect(within(periods).getByRole('button', { name: 'Desde o início' })).toHaveAttribute('aria-pressed', 'true');
    await screen.findByTestId('hero-twr');

    await userEvent.click(within(periods).getByRole('button', { name: 'No ano' }));

    await vi.waitFor(() => expect(plain(screen.getByTestId('hero-twr'))).toBe('-3,63%'));
    expect(within(periods).getByRole('button', { name: 'No ano' })).toHaveAttribute('aria-pressed', 'true');
    expect(screen.getByTestId('hero-twr')).toHaveClass('text-negative');
    expect(plain(screen.getByTestId('hero-annualised'))).toBe('em 266 dias');
    expect(plain(screen.getByTestId('hero-benchmark-IPCA6'))).toContain('Sem dados');
    expect(returnsCalls(seen)).toEqual(['/api/returns/portfolio?period=inception', '/api/returns/portfolio?period=ytd']);
  });

  it('says so when nothing was valued in the period', async () => {
    stubInvestments(
      () => [petr4],
      (_request, url) =>
        url.pathname === '/api/returns/portfolio' ? { body: { ...sinceInception, period: null, twr: null, xirr: null, timingEffect: null, series: [] } } : undefined,
    );
    renderAt('/investments');

    expect(await screen.findByText('Nenhuma posição valorizada neste período.')).toBeInTheDocument();
    expect(screen.queryByTestId('hero-twr')).not.toBeInTheDocument();
  });

  it('asks for no returns while nothing is held', async () => {
    const seen = stubInvestments(() => []);
    renderAt('/investments');

    expect(await screen.findByText('Nenhum ativo na carteira ainda.')).toBeInTheDocument();
    expect(screen.queryByTestId('returns-hero')).not.toBeInTheDocument();
    expect(returnsCalls(seen)).toEqual([]);
  });
});

/** A position with a result, for the ranking: its percentage is over its cost. */
const withResult = (ticker: string, assetClass: Position['class'], unrealisedBrl: number, valueBrl: number): Position => ({
  ...petr4,
  assetId: `a-${ticker.toLowerCase()}`,
  ticker,
  name: `${ticker} nome`,
  class: assetClass,
  valueBrl,
  costBasisBrl: valueBrl - unrealisedBrl,
  unrealisedBrl,
  unrealisedPct: Number((unrealisedBrl / (valueBrl - unrealisedBrl)).toFixed(4)),
});

/** Ten open positions, a closed one and one not yet valued; MXRF11's loss outweighs four gains. */
const twelve: Position[] = [
  withResult('IRFM11', 'EtfBr', 5489.03, 39600),
  withResult('BTC', 'Crypto', 21145.63, 38790),
  withResult('IMAB11', 'EtfBr', 5067.15, 38269),
  withResult('VOO', 'StockUs', 5893.25, 19396),
  withResult('MXRF11', 'Fii', -3000, 14240),
  withResult('ITUB4', 'StockBr', 2252.36, 11940),
  withResult('MSFT', 'StockUs', 2438.93, 10090),
  withResult('AAPL', 'StockUs', 1773.61, 9350),
  withResult('BBAS3', 'StockBr', 100, 5000),
  withResult('WEGE3', 'StockBr', -50, 3000),
  soldOut,
  justAdded,
];

describe('InvestmentsPage: contributions and holdings (016)', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  /** Spec 016 web test 8. */
  it('ranks the eight largest results, gains and losses alike, largest gain first, each linked to its asset', async () => {
    stubInvestments(() => twelve);
    renderAt('/investments');

    const card = await screen.findByTestId('contributors');
    expect(within(card).getByRole('heading', { name: 'O que mais contribuiu' })).toBeInTheDocument();
    const rows = within(card).getAllByTestId(/^contributor-a-/);
    expect(rows.map((row) => row.dataset.testid)).toEqual([
      'contributor-a-btc',
      'contributor-a-voo',
      'contributor-a-irfm11',
      'contributor-a-imab11',
      'contributor-a-msft',
      'contributor-a-itub4',
      'contributor-a-aapl',
      'contributor-a-mxrf11',
    ]);

    const btc = within(card).getByTestId('contributor-a-btc');
    expect(within(btc).getByRole('link', { name: 'BTC' })).toHaveAttribute('href', '/investments/a-btc');
    expect(btc).toHaveTextContent('Criptomoeda');
    expect(plain(btc)).toContain('+R$ 21.145,63');
    expect(plain(btc)).toContain('+119,84% · R$ 39 mil');
    expect(within(btc).getByTestId('result-bar')).toHaveStyle({ width: '100%' });
    expect(within(btc).getByText('+R$ 21.145,63')).toHaveClass('text-positive');

    const loss = within(card).getByTestId('contributor-a-mxrf11');
    expect(within(loss).getByText('-R$ 3.000,00')).toHaveClass('text-negative');
    expect(plain(loss)).toContain('-17,40% · R$ 14 mil');

    expect(within(card).getByRole('link', { name: 'Todas as 10 posições' })).toHaveAttribute('href', '/investments#posicoes');
  });

  /** Spec 016 web test 9. */
  it('shows what is invested, its result over cost, and the allocation by class', async () => {
    stubInvestments(() => [aapl, petr4]);
    renderAt('/investments');

    const card = await screen.findByTestId('holdings');
    expect(within(card).getByRole('heading', { name: 'Patrimônio investido' })).toBeInTheDocument();
    expect(plain(within(card).getByTestId('holdings-total'))).toBe('R$ 14.510,00');
    const result = within(card).getByTestId('holdings-result');
    expect(plain(result)).toBe('+R$ 3.297,66 sobre o custo');
    expect(result).toHaveClass('text-positive');

    const classes = within(card).getAllByTestId(/^allocation-/);
    expect(classes.map((item) => plain(item))).toEqual(['Ação (EUA)75,8%R$ 11 mil', 'Ação (B3)24,2%R$ 3,5 mil']);
    expect(within(card).getByTestId('stacked-allocation').children).toHaveLength(2);
  });

  it('has neither card while nothing is open', async () => {
    stubInvestments(() => [soldOut]);
    renderAt('/investments');

    expect(await screen.findByText('Nenhuma posição em aberto.')).toBeInTheDocument();
    expect(screen.queryByTestId('contributors')).not.toBeInTheDocument();
    expect(screen.queryByTestId('holdings')).not.toBeInTheDocument();
  });
});

describe('InvestmentsPage: in dollars (016)', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    act(() => chooseCurrency('BRL'));
    localStorage.clear();
  });

  /** Spec 016 web test 11, the positions. */
  it('converts every money figure at the latest rate, prices stay in their currency, and says which rate', async () => {
    localStorage.setItem('currency', 'USD');
    stubInvestments(() => [aapl, petr4]);
    renderAt('/investments');

    const row = await screen.findByTestId('position-a-petr4');
    expect(plain(row)).toContain('US$ 638,18');
    expect(plain(row)).toContain('+US$ 54,12');
    expect(plain(row)).toContain('+9,27%');
    expect(plain(row)).toContain('R$ 35,10');
    expect(plain(screen.getByTestId('position-a-aapl'))).toContain('US$ 2.000,00');

    const total = screen.getByTestId('positions-total');
    expect(plain(total)).toContain('US$ 2.638,18');
    expect(plain(total)).toContain('+US$ 599,57');
    expect(plain(screen.getByTestId('currency-note'))).toBe('em dólar · US$ 1 = R$ 5,50 em 23/09');

    const holdings = screen.getByTestId('holdings');
    expect(plain(within(holdings).getByTestId('holdings-total'))).toBe('US$ 2.638,18');
    expect(plain(within(holdings).getByTestId('holdings-result'))).toBe('+US$ 599,57 sobre o custo');
    expect(within(holdings).getAllByTestId(/^allocation-/).map((item) => plain(item))).toEqual([
      'Ação (EUA)75,8%US$ 2 mil',
      'Ação (B3)24,2%US$ 638',
    ]);
    expect(plain(screen.getByTestId('contributor-a-aapl'))).toContain('+US$ 545,45');
  });

  /** Spec 016 web test 5, on the page. */
  it('switches with the R$ | US$ toggle, R$ first, and remembers the choice', async () => {
    stubInvestments(() => [petr4]);
    renderAt('/investments');

    const toggle = await screen.findByRole('group', { name: 'Moeda' });
    expect(within(toggle).getAllByRole('button').map((button) => button.textContent)).toEqual(['R$', 'US$']);
    expect(within(toggle).getByRole('button', { name: 'R$' })).toHaveAttribute('aria-pressed', 'true');
    expect(plain(await screen.findByTestId('position-a-petr4'))).toContain('R$ 3.510,00');

    await userEvent.click(within(toggle).getByRole('button', { name: 'US$' }));

    expect(within(toggle).getByRole('button', { name: 'US$' })).toHaveAttribute('aria-pressed', 'true');
    expect(localStorage.getItem('currency')).toBe('USD');
    expect(plain(screen.getByTestId('position-a-petr4'))).toContain('US$ 638,18');
  });

  it('stays in reais, and says why, while no dollar rate was ever synced', async () => {
    localStorage.setItem('currency', 'USD');
    stubInvestments(
      () => [petr4],
      (_request, url) => (url.pathname === '/api/investments/summary' ? { body: { ...summary, usdBrl: null } } : undefined),
    );
    renderAt('/investments');

    expect(await screen.findByText('Sem cotação do dólar sincronizada; valores em reais.')).toBeInTheDocument();
    expect(plain(screen.getByTestId('position-a-petr4'))).toContain('R$ 3.510,00');
  });

  /** Spec 016 web test 11, the hero. */
  it('shows the return in dollars, derived from the dollar\'s index, and the XIRR in reais', async () => {
    localStorage.setItem('currency', 'USD');
    stubInvestments(() => [petr4]);
    renderAt('/investments');

    const hero = await screen.findByTestId('returns-hero');
    expect(await within(hero).findByRole('heading', { name: 'Rentabilidade da carteira em dólar · desde 04/10/2021' })).toBeInTheDocument();
    // 184.23 / 101 - 1.
    expect(plain(within(hero).getByTestId('hero-twr'))).toBe('+82,41%');
    expect(plain(within(hero).getByTestId('hero-xirr'))).toBe(
      'Retorno do seu dinheiro (considerando quando você aportou): +14,14% a.a. (em reais)',
    );
    // 184.41 / 101 - 1, and the difference from the dollar figures.
    const cdi = within(hero).getByTestId('hero-benchmark-CDI');
    expect(plain(cdi)).toContain('vs CDI +82,58%');
    expect(cdi).toHaveTextContent('-0,18 p.p.');
  });

  it('keeps the hero in reais, and says why, when the dollar could not anchor the period', async () => {
    localStorage.setItem('currency', 'USD');
    stubInvestments(
      () => [petr4],
      (_request, url) => (url.pathname === '/api/returns/portfolio' ? { body: thisYear } : undefined),
    );
    renderAt('/investments');

    expect(await screen.findByText('Rentabilidade em reais: sem cotação do dólar no início do período.')).toBeInTheDocument();
    expect(plain(screen.getByTestId('hero-twr'))).toBe('-3,63%');
    expect(screen.getByRole('heading', { name: 'Rentabilidade da carteira · desde 01/01/2026' })).toBeInTheDocument();
  });
});
