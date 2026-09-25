import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { RouterProvider, createMemoryHistory, createRouter } from '@tanstack/react-router';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';

import type { SyncRun } from '@/api/finance';
import { routeTree } from '@/routeTree';
import { stubFetch, type SeenRequest } from '@/test-utils';

const me = { id: 'u1', email: 'ada@example.com', displayName: 'Ada Lovelace', aiEnabled: false };

const rateLimited = 'O provedor recusou por excesso de requisições; tente mais tarde.';

const finished: SyncRun = {
  id: 'run-ok',
  startedAt: '2026-09-24T06:00:00Z',
  finishedAt: '2026-09-24T06:01:10Z',
  trigger: 'Scheduled',
  status: 'Succeeded',
  summary: {
    Brapi: { rowsWritten: 1250, itemsSynced: 2, itemsFailed: 0, error: null, failures: [] },
    Bcb: { rowsWritten: 1, itemsSynced: 1, itemsFailed: 0, error: null, failures: [] },
  },
};

const partial: SyncRun = {
  id: 'run-partial',
  startedAt: '2026-09-23T06:00:00Z',
  finishedAt: '2026-09-23T06:02:00Z',
  trigger: 'Manual',
  status: 'PartialFailure',
  summary: {
    CoinGecko: {
      rowsWritten: 0,
      itemsSynced: 0,
      itemsFailed: 1,
      error: rateLimited,
      failures: [{ item: 'BTC', error: rateLimited }],
    },
    TwelveData: { rowsWritten: 3, itemsSynced: 1, itemsFailed: 0, error: null, failures: [] },
  },
};

type Answer = { status?: number; body?: unknown };

function stubApi(runs: () => SyncRun[], sync: () => Answer = () => ({ status: 202, body: { syncRunId: 'run-new' } })) {
  return stubFetch((request: SeenRequest) => {
    const url = new URL(request.url, 'http://localhost');

    switch (`${request.method} ${url.pathname}`) {
      case 'GET /api/auth/me':
        return { body: me };
      case 'GET /api/market-data/sync-runs':
        return { body: runs() };
      case 'POST /api/market-data/sync':
        return sync();
      case 'GET /api/market-data/assets':
        return { body: [] };
      default:
        return undefined;
    }
  });
}

function renderPage() {
  const router = createRouter({ routeTree, history: createMemoryHistory({ initialEntries: ['/market-data'] }) });
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  render(
    <QueryClientProvider client={client}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  );
}

describe('MarketDataPage: sync runs', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  /** Spec web unit test 25. */
  it('renders each run status with its per-provider breakdown', async () => {
    stubApi(() => [finished, partial]);
    renderPage();

    const ok = await screen.findByTestId('sync-run-run-ok');
    expect(within(ok).getByTestId('sync-run-status')).toHaveTextContent('Concluída');
    expect(within(ok).getByText('Agendada')).toBeInTheDocument();
    const brapi = within(ok).getByTestId('provider-summary-Brapi');
    expect(brapi).toHaveTextContent('brapi');
    expect(brapi).toHaveTextContent('1.250 linhas gravadas');
    expect(brapi).toHaveTextContent('2 itens sincronizados');
    expect(within(ok).getByTestId('provider-summary-Bcb')).toHaveTextContent('Banco Central (SGS)');
    expect(within(ok).getByTestId('provider-summary-Bcb')).toHaveTextContent('1 linha gravada');

    const failed = screen.getByTestId('sync-run-run-partial');
    expect(within(failed).getByTestId('sync-run-status')).toHaveTextContent('Concluída com falhas');
    expect(within(failed).getByText('Manual')).toBeInTheDocument();
    const coinGecko = within(failed).getByTestId('provider-summary-CoinGecko');
    expect(coinGecko).toHaveTextContent('1 com falha');
    expect(coinGecko).toHaveTextContent(`BTC: ${rateLimited}`);
    expect(within(failed).getByTestId('provider-summary-TwelveData')).toHaveTextContent('Twelve Data');

    // Newest first, as the API sends them.
    const rows = screen.getAllByTestId(/^sync-run-run-/);
    expect(rows.map((row) => row.dataset.testid)).toEqual(['sync-run-run-ok', 'sync-run-run-partial']);
  });

  /** Spec 019 web test 23: the API's pt-BR reason reaches the screen as sent. */
  it('shows a missing key as the API explains it, under the ticker it stopped', async () => {
    const missing = 'O brapi exige um token para BBAS3. Configure MarketData:Brapi:Token.';
    stubApi(() => [
      {
        ...partial,
        id: 'run-keyless',
        summary: {
          Brapi: { rowsWritten: 1247, itemsSynced: 1, itemsFailed: 1, error: missing, failures: [{ item: 'BBAS3', error: missing }] },
          Binance: { rowsWritten: 1826, itemsSynced: 1, itemsFailed: 0, error: null, failures: [] },
        },
      },
    ]);
    renderPage();

    const brapi = await screen.findByTestId('provider-summary-Brapi');
    expect(brapi).toHaveTextContent(`BBAS3: ${missing}`);
    expect(brapi).toHaveTextContent('1 com falha');
    expect(screen.getByTestId('provider-summary-Binance')).toHaveTextContent('Binance · 1.826 linhas gravadas');
  });

  it('says so when no sync has run yet', async () => {
    stubApi(() => []);
    renderPage();

    expect(await screen.findByText('Nenhuma sincronização ainda.')).toBeInTheDocument();
  });

  it('triggers a sync and follows the new run until it leaves Running', async () => {
    let runs: SyncRun[] = [finished];
    let reads = 0;
    const seen = stubApi(() => {
      reads += 1;
      return runs;
    });
    renderPage();
    await screen.findByTestId('sync-run-run-ok');

    const running: SyncRun = {
      ...finished,
      id: 'run-new',
      trigger: 'Manual',
      status: 'Running',
      finishedAt: null,
      summary: {},
      startedAt: '2026-09-24T12:00:00Z',
    };
    runs = [running, finished];
    await userEvent.click(screen.getByRole('button', { name: 'Sincronizar agora' }));

    expect(seen.filter((request) => request.method === 'POST')).toHaveLength(1);
    const row = await screen.findByTestId('sync-run-run-new');
    expect(within(row).getByTestId('sync-run-status')).toHaveTextContent('Em andamento');

    const readsWhileRunning = reads;
    runs = [{ ...running, status: 'Succeeded', finishedAt: '2026-09-24T12:00:05Z', summary: finished.summary }, finished];

    // Polled while the newest run is Running, without another click.
    expect(
      await within(await screen.findByTestId('sync-run-run-new')).findByText('Concluída', {}, { timeout: 4000 }),
    ).toBeInTheDocument();
    expect(reads).toBeGreaterThan(readsWhileRunning);
  });

  it('shows the API refusal of a sync verbatim', async () => {
    const detail = 'Uma sincronização foi iniciada há menos de 10 minutos ou ainda está em andamento. Tente novamente em 7 minutos.';
    stubApi(
      () => [finished],
      () => ({ status: 429, body: { title: 'Muitas solicitações', status: 429, detail } }),
    );
    renderPage();
    await screen.findByTestId('sync-run-run-ok');

    await userEvent.click(screen.getByRole('button', { name: 'Sincronizar agora' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(detail);
  });
});
