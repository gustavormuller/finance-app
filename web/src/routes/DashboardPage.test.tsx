import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { RouterProvider, createMemoryHistory, createRouter } from '@tanstack/react-router';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import type { CategoryTotal, DashboardSummary, MonthTotals, PortfolioSummary } from '@/api/finance';
import { routeTree } from '@/routeTree';
import { stubFetch, type SeenRequest } from '@/test-utils';

const me = { id: 'u1', email: 'ada@example.com', displayName: 'Ada Lovelace', aiEnabled: false };

const summary: DashboardSummary = {
  balances: [
    {
      accountId: 'acc-checking',
      name: 'Conta Itaú',
      type: 'Checking',
      currency: 'BRL',
      balance: 2500,
      excludedFromTotal: false,
    },
    {
      accountId: 'acc-card',
      name: 'Nubank',
      type: 'CreditCard',
      currency: 'BRL',
      balance: -1500,
      excludedFromTotal: false,
    },
  ],
  total: 1000,
  month: { income: 5000, expense: -3200.5, net: 1799.5 },
};

/** Twelve months ending September 2026, zeros as the API sends them. */
function series(filled: (month: string) => Omit<MonthTotals, 'month'>): MonthTotals[] {
  return Array.from({ length: 13 }, (_, index) => {
    const date = new Date(2025, 8 + index, 1);
    const month = `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}`;

    return { month, ...filled(month) };
  });
}

const expenses: CategoryTotal[] = [
  { categoryId: 'c-food', name: 'Alimentação', amount: -2000, share: 0.625 },
  { categoryId: 'c-home', name: 'Moradia', amount: -1200, share: 0.375 },
];

const incomes: CategoryTotal[] = [{ categoryId: 'c-salary', name: 'Salário', amount: 5000, share: 1 }];

const noPortfolio: PortfolioSummary = { totalBrl: 0, totalCostBrl: 0, unrealisedBrl: 0 };

function stubApi(
  overrides: { summary?: DashboardSummary; monthly?: MonthTotals[]; portfolio?: PortfolioSummary } = {},
) {
  return stubFetch((request: SeenRequest) => {
    const url = new URL(request.url, 'http://localhost');

    switch (url.pathname) {
      case '/api/auth/me':
        return { body: me };
      case '/api/dashboard/summary':
        return { body: overrides.summary ?? summary };
      case '/api/dashboard/monthly':
        return { body: overrides.monthly ?? series(() => ({ income: 5000, expense: -3200.5 })) };
      case '/api/dashboard/by-category':
        return { body: url.searchParams.get('kind') === 'Income' ? incomes : expenses };
      case '/api/transactions':
        return { body: { items: [], page: 1, pageSize: 10, total: 0 } };
      case '/api/ai/analyses':
        return { body: [] };
      case '/api/investments/summary':
        return { body: overrides.portfolio ?? noPortfolio };
      default:
        return undefined;
    }
  });
}

function renderDashboard() {
  const router = createRouter({ routeTree, history: createMemoryHistory({ initialEntries: ['/'] }) });
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  render(
    <QueryClientProvider client={client}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  );
}

const requestsTo = (seen: SeenRequest[], path: string) =>
  seen.map((request) => new URL(request.url, 'http://localhost')).filter((url) => url.pathname === path);

describe('DashboardPage', () => {
  beforeEach(() => {
    // Only Date: faking timers too would stall user-event and the query client.
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date(2026, 8, 15, 12, 0));
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  /** Spec web unit test 20. */
  it('renders a credit card balance negative, in the distinct style', async () => {
    stubApi();
    renderDashboard();

    const card = await screen.findByTestId('account-balance-acc-card');
    const cardAmount = within(card).getByTestId('amount');
    const checkingAmount = within(screen.getByTestId('account-balance-acc-checking')).getByTestId('amount');

    expect(cardAmount).toHaveTextContent('−1.500,00');
    expect(cardAmount).toHaveClass('text-destructive');
    expect(checkingAmount).not.toHaveClass('text-destructive');
    expect(within(card).getByText('Cartão de crédito')).toBeInTheDocument();
  });

  /** Spec web unit test 21. The month is always sent, computed from the local date. */
  it('changes the month query parameter with the month selector', async () => {
    const seen = stubApi();
    renderDashboard();

    expect(await screen.findByTestId('selected-month')).toHaveTextContent('setembro de 2026');
    await waitFor(() =>
      expect(requestsTo(seen, '/api/dashboard/summary').map((url) => url.searchParams.get('month'))).toEqual([
        '2026-09',
      ]),
    );

    await userEvent.setup().click(screen.getByRole('button', { name: 'Mês anterior' }));

    expect(screen.getByTestId('selected-month')).toHaveTextContent('agosto de 2026');
    await waitFor(() => {
      expect(requestsTo(seen, '/api/dashboard/summary').at(-1)?.searchParams.get('month')).toBe('2026-08');
      expect(requestsTo(seen, '/api/dashboard/by-category').at(-1)?.searchParams.get('month')).toBe('2026-08');
    });
    expect(screen.getByRole('button', { name: 'Próximo mês' })).toBeEnabled();
  });

  /** Spec web unit test 22. */
  it('switches the category chart with the kind toggle', async () => {
    const seen = stubApi();
    renderDashboard();

    const breakdown = await screen.findByTestId('category-breakdown');
    expect(await within(breakdown).findByText('Alimentação')).toBeInTheDocument();
    expect(within(breakdown).getByText('62,5%')).toBeInTheDocument();

    await userEvent.setup().click(within(breakdown).getByRole('button', { name: 'Receitas' }));

    expect(await within(breakdown).findByText('Salário')).toBeInTheDocument();
    expect(within(breakdown).queryByText('Alimentação')).not.toBeInTheDocument();
    expect(within(breakdown).getByRole('button', { name: 'Receitas' })).toHaveAttribute('aria-pressed', 'true');
    expect(requestsTo(seen, '/api/dashboard/by-category').at(-1)?.searchParams.get('kind')).toBe('Income');
  });

  /** Spec web unit test 23. */
  it('shows the empty state when there are no balances and every month is zero', async () => {
    stubApi({
      summary: { balances: [], total: 0, month: { income: 0, expense: 0, net: 0 } },
      monthly: series(() => ({ income: 0, expense: 0 })),
    });
    renderDashboard();

    const empty = await screen.findByTestId('dashboard-empty');
    expect(within(empty).getByRole('link', { name: /lançamento/i })).toHaveAttribute('href', '/transactions');
    // 015: an import starts from an account, and with none yet that is the first step.
    expect(within(empty).getByRole('link', { name: /importar/i })).toHaveAttribute('href', '/accounts');
    expect(screen.queryByTestId('total-balance')).not.toBeInTheDocument();
  });

  /** An account with no transactions yet is not the empty state: its balance is news. */
  it('shows the dashboard when accounts exist even if every month is zero', async () => {
    stubApi({ monthly: series(() => ({ income: 0, expense: 0 })) });
    renderDashboard();

    expect(await screen.findByTestId('total-balance')).toHaveTextContent('1.000,00');
    expect(screen.queryByTestId('dashboard-empty')).not.toBeInTheDocument();
  });

  /** 009: the "Análise do mês" card follows the month selector. */
  it('shows the analysis card for the selected month', async () => {
    const seen = stubApi();
    renderDashboard();

    const card = await screen.findByTestId('analysis-card');
    expect(await within(card).findByText(/Nenhuma análise de setembro de 2026 ainda/)).toBeInTheDocument();

    await userEvent.setup().click(screen.getByRole('button', { name: 'Mês anterior' }));

    expect(await within(screen.getByTestId('analysis-card')).findByText(/Nenhuma análise de agosto de 2026 ainda/)).toBeInTheDocument();
    expect(requestsTo(seen, '/api/ai/analyses').map((url) => url.searchParams.get('month'))).toEqual([
      '2026-09',
      '2026-08',
    ]);
  });

  /** Spec 012 web unit test 6: the hero adds what is invested, only when something is. */
  it('shows the invested total in the hero when there are positions, and not otherwise', async () => {
    stubApi({ portfolio: { totalBrl: 25300, totalCostBrl: 19014.1, unrealisedBrl: 6285.9 } });
    renderDashboard();

    const invested = await screen.findByTestId('hero-invested');
    expect(invested).toHaveTextContent('25.300,00');
    expect(invested).toHaveTextContent('6.285,90');

    vi.unstubAllGlobals();
    document.body.innerHTML = '';
    stubApi();
    renderDashboard();

    await screen.findByTestId('total-balance');
    await waitFor(() => expect(screen.queryByTestId('hero-invested')).not.toBeInTheDocument());
  });
});
