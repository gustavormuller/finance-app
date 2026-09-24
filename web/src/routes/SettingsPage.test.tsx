import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { RouterProvider, createMemoryHistory, createRouter } from '@tanstack/react-router';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';

import type { AiUsage } from '@/api/finance';
import { routeTree } from '@/routeTree';
import { stubFetch, type SeenRequest } from '@/test-utils';

const usage: AiUsage = { month: '2026-09', spentBrl: 0.1234, budgetBrl: 15, calls: 3 };

function stubApi({ aiEnabled = false, patch }: { aiEnabled?: boolean; patch?: { status: number; body: unknown } } = {}) {
  let me = { id: 'u1', email: 'ada@example.com', displayName: 'Ada Lovelace', aiEnabled };

  return stubFetch((request: SeenRequest) => {
    const path = new URL(request.url, 'http://localhost').pathname;

    if (path === '/api/auth/me' && request.method === 'PATCH') {
      if (patch) {
        return patch;
      }

      me = { ...me, ...(request.body as { aiEnabled: boolean }) };
      return { body: me };
    }

    switch (path) {
      case '/api/auth/me':
        return { body: me };
      case '/api/ai/usage':
        return { body: usage };
      default:
        return undefined;
    }
  });
}

function renderSettings() {
  const router = createRouter({ routeTree, history: createMemoryHistory({ initialEntries: ['/settings'] }) });
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  render(
    <QueryClientProvider client={client}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  );
}

describe('SettingsPage', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('is in the navigation', async () => {
    stubApi();
    renderSettings();

    expect(await screen.findByRole('link', { name: 'Configurações' })).toHaveAttribute('href', '/settings');
    expect(screen.getByRole('heading', { name: 'Configurações' })).toBeInTheDocument();
  });

  it('shows the toggle off, and this month\'s spend against the budget', async () => {
    stubApi();
    renderSettings();

    expect(await screen.findByRole('switch', { name: 'Usar IA nesta conta' })).not.toBeChecked();
    expect(await screen.findByTestId('ai-spend')).toHaveTextContent('R$ 0,12 de R$ 15,00 em setembro de 2026 · 3 chamadas');
  });

  /** Decision 10: what leaves the server, and to whom, beside the toggle. */
  it('says what each feature sends to the provider, and what it does not', async () => {
    stubApi();
    renderSettings();

    const disclosure = await screen.findByTestId('ai-disclosure');

    for (const phrase of [
      'Anthropic (Claude) ou a OpenAI (ChatGPT)',
      'Com a IA desligada, nada é enviado.',
      'normalizada: em maiúsculas, sem acentos e sem números',
      'se cada uma dessas linhas é um débito ou um crédito',
      'os nomes das suas categorias.',
      'Não são enviados valores, datas nem contas.',
      'os nomes, tipos e saldos das suas contas',
      'nos três últimos meses',
      'Um PIX enviado a uma pessoa leva o nome dela.',
      'os três totais da sua carteira de investimentos',
      'Não são enviados lançamentos um a um, descrições originais, datas nem descrições de receitas.',
    ]) {
      expect(disclosure).toHaveTextContent(phrase);
    }
  });

  it('turns AI on with PATCH /api/auth/me and shows the new state', async () => {
    const seen = stubApi();
    renderSettings();

    const toggle = await screen.findByRole('switch', { name: 'Usar IA nesta conta' });
    await userEvent.setup().click(toggle);

    await waitFor(() => expect(toggle).toBeChecked());
    expect(screen.getByTestId('ai-state')).toHaveTextContent('Ligada');
    expect(seen.filter((request) => request.method === 'PATCH')).toEqual([
      { method: 'PATCH', url: '/api/auth/me', body: { aiEnabled: true } },
    ]);
  });

  it('turns AI off and keeps the state when the API refuses', async () => {
    stubApi({
      aiEnabled: true,
      patch: { status: 400, body: { title: 'Inválido', detail: 'Informe se a IA deve ficar ligada ou desligada.' } },
    });
    renderSettings();

    const toggle = await screen.findByRole('switch', { name: 'Usar IA nesta conta' });
    expect(toggle).toBeChecked();

    await userEvent.setup().click(toggle);

    expect(await screen.findByRole('alert')).toHaveTextContent('Informe se a IA deve ficar ligada ou desligada.');
    expect(toggle).toBeChecked();
  });

  /** Found in a real browser: a switch that only moves after the round trip reads as broken. */
  it('moves the switch at once, while the PATCH is in flight', async () => {
    let release: () => void = () => {};
    const answered = new Promise<void>((resolve) => (release = resolve));
    const me = { id: 'u1', email: 'ada@example.com', displayName: 'Ada Lovelace', aiEnabled: false };

    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
        const path = new URL(input.toString(), 'http://localhost').pathname;
        const json = (body: unknown) => new Response(JSON.stringify(body), { headers: { 'Content-Type': 'application/json' } });

        if (path === '/api/auth/me' && init?.method === 'PATCH') {
          await answered;
          return json({ ...me, aiEnabled: true });
        }

        return json(path === '/api/ai/usage' ? usage : me);
      }),
    );
    renderSettings();

    const toggle = await screen.findByRole('switch', { name: 'Usar IA nesta conta' });
    await userEvent.setup().click(toggle);

    expect(toggle).toBeChecked();
    release();
    await waitFor(() => expect(toggle).toBeEnabled());
    expect(toggle).toBeChecked();
  });
});
