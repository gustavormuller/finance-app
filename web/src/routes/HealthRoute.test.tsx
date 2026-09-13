import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';

import HealthRoute from './HealthRoute';

function mockHealthResponse(status: number, body: unknown) {
  vi.stubGlobal(
    'fetch',
    vi.fn(
      async () =>
        new Response(JSON.stringify(body), {
          status,
          headers: { 'Content-Type': 'application/json' },
        }),
    ),
  );
}

function renderRoute() {
  // Retries off: a retrying query would keep the degraded case in flight and the
  // assertion would time out instead of failing on the rendered text.
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });

  return render(
    <QueryClientProvider client={queryClient}>
      <HealthRoute />
    </QueryClientProvider>,
  );
}

describe('HealthRoute', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('renders the status and database reported by a healthy API', async () => {
    mockHealthResponse(200, { status: 'ok', database: 'ok' });

    renderRoute();

    expect(await screen.findByTestId('health-status')).toHaveTextContent('ok');
    expect(screen.getByTestId('health-database')).toHaveTextContent('ok');
  });

  it('renders a degraded state when the API reports the database is unreachable', async () => {
    mockHealthResponse(503, { status: 'degraded', database: 'unreachable' });

    renderRoute();

    expect(await screen.findByTestId('health-status')).toHaveTextContent('degraded');
    expect(screen.getByTestId('health-database')).toHaveTextContent('unreachable');
  });
});
