import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { RouterProvider, createMemoryHistory, createRouter } from '@tanstack/react-router';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';

import type { Position, Returns } from '@/api/finance';
import { routeTree } from '@/routeTree';
import { stubFetch, type SeenRequest } from '@/test-utils';

const me = { id: 'u1', email: 'ada@example.com', displayName: 'Ada Lovelace', aiEnabled: false };

const plain = (element: HTMLElement) => (element.textContent ?? '').replace(/\u00a0/g, ' ');

/** Six months: the period's TWR, with XIRR and the timing effect a year's rate. */
const halfYear: Returns = {
  period: { from: '2026-03-01', to: '2026-08-31', days: 184 },
  twr: { total: 0.0512, annualised: 0.1043 },
  xirr: 0.0831,
  timingEffect: -0.0212,
  benchmarks: {
    CDI: { total: 0.0321, annualised: 0.0647 },
    IPCA6: null,
    IVVB11: { total: 0.0712, annualised: 0.1459 },
    SELIC: { total: 0.0322, annualised: 0.0649 },
    USDBRL: { total: -0.0215, annualised: -0.0421 },
  },
  series: [
    { date: '2026-02-28', portfolio: 100, CDI: 100, IVVB11: 100, SELIC: 100, USDBRL: 100 },
    { date: '2026-08-31', portfolio: 105.12, CDI: 103.21, IVVB11: 107.12, SELIC: 103.22, USDBRL: 97.85 },
  ],
};

const threeYears: Returns = {
  ...halfYear,
  period: { from: '2023-09-01', to: '2026-08-31', days: 1096 },
  twr: { total: 0.331, annualised: 0.1 },
  xirr: 0.1234,
  timingEffect: 0.0234,
};

const nothingHeld: Returns = {
  period: null,
  twr: null,
  xirr: null,
  timingEffect: null,
  benchmarks: { CDI: null, IPCA6: null, IVVB11: null, SELIC: null, USDBRL: null },
  series: [],
};

type Answer = { status?: number; body?: unknown };

function stubReturns(
  portfolio: (url: URL) => Answer,
  positions: Position[] = [],
  asset: (id: string, url: URL) => Answer | undefined = () => undefined,
) {
  return stubFetch((request: SeenRequest) => {
    const url = new URL(request.url, 'http://localhost');
    const assetId = /^\/api\/returns\/assets\/([^/]+)$/.exec(url.pathname)?.[1];
    if (assetId) {
      return asset(assetId, url);
    }

    switch (`${request.method} ${url.pathname}`) {
      case 'GET /api/auth/me':
        return { body: me };
      case 'GET /api/returns/portfolio':
        return portfolio(url);
      case 'GET /api/investments/assets':
        return { body: positions };
      case 'GET /api/investments/summary':
        return { body: { totalBrl: 0, totalCostBrl: 0, unrealisedBrl: 0 } };
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

  const view = render(
    <QueryClientProvider client={client}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  );

  return { router, container: view.container };
}

const returnsCalls = (seen: SeenRequest[]) =>
  seen.filter((request) => request.url.startsWith('/api/returns/portfolio')).map((request) => request.url);

describe('ReturnsPage', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('is linked from the positions page', async () => {
    stubReturns(() => ({ body: halfYear }));
    renderAt('/investments');

    const link = await screen.findByRole('link', { name: 'Rentabilidade' });
    expect(link).toHaveAttribute('href', '/investments/returns');
  });

  /** Spec web unit test 33, a loss from timing. */
  it('shows the timing effect with its sentence, in the expense colour when negative', async () => {
    stubReturns(() => ({ body: halfYear }));
    renderAt('/investments/returns');

    const timing = await screen.findByTestId('headline-timing');
    expect(timing).toHaveTextContent('Efeito do timing');
    expect(timing).toHaveTextContent('Quanto suas decisões de quando aportar ajudaram ou atrapalharam.');
    const value = within(timing).getByTestId('headline-value');
    expect(plain(value)).toBe('-2,12% a.a.');
    expect(value).toHaveClass('text-chart-expense');
    expect(value).not.toHaveClass('text-green-700');
  });

  /** Spec web unit test 33, a gain from timing. */
  it('colours a positive timing effect green', async () => {
    stubReturns(() => ({ body: threeYears }));
    renderAt('/investments/returns');

    const value = within(await screen.findByTestId('headline-timing')).getByTestId('headline-value');
    expect(plain(value)).toBe('+2,34% a.a.');
    expect(value).toHaveClass('text-green-700');
    expect(value).not.toHaveClass('text-chart-expense');
  });

  it('shows only the period TWR for a year or less, and XIRR a year\'s rate', async () => {
    stubReturns(() => ({ body: halfYear }));
    renderAt('/investments/returns');

    const twr = await screen.findByTestId('headline-twr');
    expect(plain(twr)).toContain('+5,12% no período');
    expect(plain(twr)).not.toContain('10,43%');
    expect(plain(screen.getByTestId('headline-xirr'))).toContain('+8,31% a.a.');
    expect(plain(screen.getByTestId('returns-period'))).toContain('01/03/2026 a 31/08/2026');
  });

  it('adds the annualised TWR past a year', async () => {
    stubReturns(() => ({ body: threeYears }));
    renderAt('/investments/returns');

    const twr = await screen.findByTestId('headline-twr');
    expect(plain(twr)).toContain('+33,10% no período');
    expect(plain(twr)).toContain('+10,00% a.a.');
  });

  it('writes a rate the API could not compute as "Sem dados", never NaN', async () => {
    stubReturns(() => ({ body: { ...halfYear, xirr: null, timingEffect: null } }));
    renderAt('/investments/returns');

    expect(await screen.findByTestId('headline-xirr')).toHaveTextContent('Sem dados');
    const timing = screen.getByTestId('headline-timing');
    expect(timing).toHaveTextContent('Sem dados');
    expect(within(timing).getByTestId('headline-value')).not.toHaveClass('text-green-700');
    expect(document.body).not.toHaveTextContent('NaN');
  });

  it('says so when nothing was held in the period', async () => {
    stubReturns(() => ({ body: nothingHeld }));
    renderAt('/investments/returns');

    expect(await screen.findByText('Nenhuma posição valorizada neste período.')).toBeInTheDocument();
    expect(screen.queryByTestId('headline-twr')).not.toBeInTheDocument();
  });

  /** Spec web unit test 35. */
  it('refetches when the period changes', async () => {
    const seen = stubReturns((url) => ({ body: url.searchParams.get('period') === 'ytd' ? threeYears : halfYear }));
    renderAt('/investments/returns');

    await screen.findByTestId('headline-twr');
    expect(returnsCalls(seen)).toEqual(['/api/returns/portfolio?period=inception']);
    expect(screen.getByRole('button', { name: 'Desde o início' })).toHaveAttribute('aria-pressed', 'true');

    await userEvent.click(screen.getByRole('button', { name: 'No ano' }));

    await vi.waitFor(() => expect(plain(screen.getByTestId('headline-twr'))).toContain('+33,10% no período'));
    expect(returnsCalls(seen)).toEqual(['/api/returns/portfolio?period=inception', '/api/returns/portfolio?period=ytd']);
    expect(screen.getByRole('button', { name: 'No ano' })).toHaveAttribute('aria-pressed', 'true');

    await userEvent.click(screen.getByRole('button', { name: 'Personalizado' }));
    await userEvent.type(screen.getByLabelText('De'), '2026-01-01');
    await userEvent.type(screen.getByLabelText('Até'), '2026-06-30');
    await userEvent.click(screen.getByRole('button', { name: 'Aplicar' }));

    await vi.waitFor(() =>
      expect(returnsCalls(seen).at(-1)).toBe('/api/returns/portfolio?period=custom&from=2026-01-01&to=2026-06-30'),
    );
  });

  it('shows a 400 on a custom range under the date it names, as sent', async () => {
    const order = 'A data inicial deve ser anterior ou igual à data final.';
    stubReturns((url) =>
      url.searchParams.get('period') === 'custom'
        ? { status: 400, body: { title: 'One or more validation errors occurred.', errors: { from: [order] } } }
        : { body: halfYear },
    );
    renderAt('/investments/returns');

    await screen.findByTestId('headline-twr');
    await userEvent.click(screen.getByRole('button', { name: 'Personalizado' }));
    await userEvent.type(screen.getByLabelText('De'), '2026-06-30');
    await userEvent.type(screen.getByLabelText('Até'), '2026-01-01');
    await userEvent.click(screen.getByRole('button', { name: 'Aplicar' }));

    const from = screen.getByLabelText('De').closest('div')!;
    expect(await within(from).findByText(order)).toBeInTheDocument();
    expect(screen.queryByText('One or more validation errors occurred.')).not.toBeInTheDocument();
  });
});

describe('ReturnsPage: comparison', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  const line = (container: HTMLElement, name: string) => container.querySelector(`path.recharts-line-curve[name="${name}"]`);

  /** Spec web unit test 34. */
  it('hides and shows each benchmark on the chart with its toggle', async () => {
    stubReturns(() => ({ body: halfYear }));
    const { container } = renderAt('/investments/returns');

    const chart = await screen.findByTestId('comparison-chart');
    const toggles = within(chart).getByRole('group', { name: 'Referências no gráfico' });
    // Every benchmark that could anchor, in the spec's order; IPCA + 6% had no data.
    expect(within(toggles).getAllByRole('button').map((button) => button.textContent)).toEqual([
      'CDI',
      'SELIC',
      'Dólar',
      'S&P 500 (IVVB11)',
    ]);
    await vi.waitFor(() => expect(line(container, 'Carteira')).not.toBeNull());
    expect(line(container, 'CDI')).not.toBeNull();
    expect(line(container, 'Dólar')).not.toBeNull();

    const cdi = within(toggles).getByRole('button', { name: 'CDI' });
    expect(cdi).toHaveAttribute('aria-pressed', 'true');
    await userEvent.click(cdi);

    expect(cdi).toHaveAttribute('aria-pressed', 'false');
    await vi.waitFor(() => expect(line(container, 'CDI')).toBeNull());
    expect(line(container, 'Carteira')).not.toBeNull();
    expect(line(container, 'Dólar')).not.toBeNull();

    await userEvent.click(cdi);

    expect(cdi).toHaveAttribute('aria-pressed', 'true');
    await vi.waitFor(() => expect(line(container, 'CDI')).not.toBeNull());
  });

  it('compares each benchmark\'s return with the portfolio\'s, the difference in points', async () => {
    stubReturns(() => ({ body: halfYear }));
    renderAt('/investments/returns');

    const portfolio = await screen.findByTestId('benchmark-row-portfolio');
    expect(plain(portfolio)).toContain('Carteira');
    expect(plain(portfolio)).toContain('+5,12%');

    const cdi = screen.getByTestId('benchmark-row-CDI');
    expect(plain(cdi)).toContain('+3,21%');
    expect(plain(cdi)).toContain('+1,91 p.p.');
    const dollar = screen.getByTestId('benchmark-row-USDBRL');
    expect(dollar).toHaveTextContent('Dólar');
    expect(plain(dollar)).toContain('-2,15%');
    expect(plain(dollar)).toContain('+7,27 p.p.');
    expect(screen.getByTestId('benchmark-row-IPCA6')).toHaveTextContent('IPCA + 6%Sem dadosSem dados');

    // A year or less: totals only.
    expect(within(screen.getByTestId('benchmarks-table')).queryByText('Ao ano')).not.toBeInTheDocument();
    expect(plain(cdi)).not.toContain('6,47%');
  });

  it('adds each annualised return past a year', async () => {
    stubReturns(() => ({ body: threeYears }));
    renderAt('/investments/returns');

    const cdi = await screen.findByTestId('benchmark-row-CDI');
    expect(within(screen.getByTestId('benchmarks-table')).getByText('Ao ano')).toBeInTheDocument();
    expect(plain(cdi)).toContain('+6,47%');
    expect(plain(screen.getByTestId('benchmark-row-portfolio'))).toContain('+10,00%');
    expect(plain(cdi)).toContain('+29,89 p.p.');
  });
});

const position = (assetId: string, ticker: string, currency: string): Position => ({
  assetId,
  ticker,
  name: ticker,
  class: currency === 'BRL' ? 'StockBr' : 'StockUs',
  currency,
  nickname: null,
  quantity: 10,
  averageCost: 10,
  price: 10,
  priceDate: '2026-08-31',
  valueBrl: 100,
  costBasisBrl: 100,
  unrealisedBrl: 0,
  unrealisedPct: 0,
  realisedBrl: 0,
  dividendsBrl: 0,
});

const petr4 = position('a-petr4', 'PETR4', 'BRL');
const aapl = position('a-aapl', 'AAPL', 'USD');
const itub4 = position('a-itub4', 'ITUB4', 'BRL');

const assetAnswers = (id: string): Answer | undefined =>
  ({
    'a-petr4': { body: { ...halfYear, twr: { total: 0.0312, annualised: 0.0629 }, xirr: 0.0415, fx: null } },
    'a-aapl': { body: { ...halfYear, twr: { total: 0.21, annualised: 0.4596 }, xirr: 0.4751, fx: { native: 0.1, fx: 0.1, total: 0.21 } } },
    'a-itub4': { body: { ...nothingHeld, fx: null } },
  })[id];

describe('ReturnsPage: per asset', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('lists each asset\'s TWR and XIRR, the FX split for a USD asset, and links to its own page', async () => {
    stubReturns(() => ({ body: halfYear }), [aapl, petr4, itub4], assetAnswers);
    renderAt('/investments/returns');

    const usd = await screen.findByTestId('asset-returns-a-aapl');
    expect(within(usd).getByRole('link', { name: 'AAPL' })).toHaveAttribute('href', '/investments/a-aapl/returns');
    await vi.waitFor(() => expect(plain(usd)).toContain('+21,00%'));
    expect(plain(usd)).toContain('+47,51% a.a.');
    expect(plain(usd)).toContain('+10,00%+10,00%');

    const brl = screen.getByTestId('asset-returns-a-petr4');
    await vi.waitFor(() => expect(plain(brl)).toContain('+3,12%'));
    expect(plain(brl)).toContain('+4,15% a.a.');
    expect(brl).toHaveTextContent('Ativo em reais');

    await vi.waitFor(() => expect(screen.getByTestId('asset-returns-a-itub4')).toHaveTextContent('Sem dadosSem dados'));
  });

  it('asks for every asset in the selected period', async () => {
    const seen = stubReturns(() => ({ body: halfYear }), [petr4], assetAnswers);
    renderAt('/investments/returns');

    await screen.findByTestId('asset-returns-a-petr4');
    await userEvent.click(screen.getByRole('button', { name: '12 meses' }));

    await vi.waitFor(() =>
      expect(seen.filter((request) => request.url.startsWith('/api/returns/assets/')).map((request) => request.url)).toEqual([
        '/api/returns/assets/a-petr4?period=inception',
        '/api/returns/assets/a-petr4?period=12m',
      ]),
    );
  });
});
