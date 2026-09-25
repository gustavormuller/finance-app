import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { RouterProvider, createMemoryHistory, createRouter } from '@tanstack/react-router';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';

import type { AiUsage } from '@/api/finance';
import { routeTree } from '@/routeTree';
import { stubFetch, type SeenRequest } from '@/test-utils';

const usage: AiUsage = { month: '2026-09', spentBrl: 0.1234, budgetBrl: 15, calls: 3 };

type Answer = { status: number; body: unknown };

function stubApi({ aiEnabled = false, patch, remove }: { aiEnabled?: boolean; patch?: Answer; remove?: Answer } = {}) {
  let me = { id: 'u1', email: 'ada@example.com', displayName: 'Ada Lovelace', aiEnabled };

  return stubFetch((request: SeenRequest) => {
    const path = new URL(request.url, 'http://localhost').pathname;

    if (path === '/api/auth/me' && request.method === 'DELETE') {
      return remove ?? { status: 204 };
    }

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

  return { router, client };
}

/** Types the confirmation and presses the button, as a person would. */
async function confirmDeletion(email = 'ada@example.com') {
  const user = userEvent.setup();
  await user.type(await screen.findByLabelText('Digite seu e-mail para confirmar'), email);
  await user.click(screen.getByRole('button', { name: 'Excluir definitivamente' }));
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

  /**
   * Found by E2E test 28: the in-flight state reaches the page on the query client's next
   * tick, so the controlled switch went back to off for that tick and Playwright's `check()`
   * saw no change. It has to hold the new state from the click itself.
   */
  it('holds the new state from the click itself, not from the next tick', async () => {
    stubApi();
    renderSettings();

    const toggle = await screen.findByRole('switch', { name: 'Usar IA nesta conta' });
    fireEvent.click(toggle);

    expect(toggle).toBeChecked();
    expect(screen.getByTestId('ai-state')).toHaveTextContent('Ligada');
    await waitFor(() => expect(toggle).toBeEnabled());
    expect(toggle).toBeChecked();
  });
});

describe('Excluir minha conta (023)', () => {
  afterEach(() => vi.unstubAllGlobals());

  /** Spec test 7. */
  it('is the last section of the page, and says what deleting takes with it', async () => {
    stubApi();
    renderSettings();

    const zone = await screen.findByRole('region', { name: 'Excluir minha conta' });
    expect(screen.getAllByRole('region').at(-1)).toBe(zone);
    for (const phrase of ['contas', 'lançamentos', 'importações', 'investimentos', 'análises de IA', 'Não dá para desfazer.']) {
      expect(zone).toHaveTextContent(phrase);
    }
  });

  /** Spec test 8: the signed-in address exactly, only surrounding spaces forgiven. */
  it('keeps "Excluir definitivamente" off until the signed-in e-mail is typed', async () => {
    stubApi();
    renderSettings();
    const user = userEvent.setup();

    const field = await screen.findByLabelText('Digite seu e-mail para confirmar');
    const button = screen.getByRole('button', { name: 'Excluir definitivamente' });
    expect(button).toBeDisabled();

    for (const near of ['ada@example.org', 'Ada@example.com', 'ada@example.co']) {
      await user.clear(field);
      await user.type(field, near);
      expect(button).toBeDisabled();
    }

    await user.clear(field);
    await user.type(field, '  ada@example.com ');
    expect(button).toBeEnabled();
  });

  /** Spec test 9. */
  it('deletes the account, empties the cache and lands on the login page with a notice', async () => {
    const seen = stubApi();
    const { router, client } = renderSettings();

    await confirmDeletion();

    expect(await screen.findByRole('status')).toHaveTextContent('Sua conta e todos os dados dela foram excluídos.');
    expect(router.state.location.pathname).toBe('/login');
    expect(seen.filter((request) => request.method === 'DELETE')).toEqual([
      { method: 'DELETE', url: '/api/auth/me', body: undefined },
    ]);
    expect(client.getQueryCache().getAll()).toEqual([]);
  });

  /**
   * Spec test 10. `{}` is what the client makes of a response with no body, which is how the
   * API's 401 and an unhandled 500 arrive.
   */
  it.each([
    ['a problem detail', { status: 409, body: { title: 'Conflito', detail: 'Uma frase da API.' } }, 'Uma frase da API.'],
    ['a 401', { status: 401, body: {} }, 'Sua sessão terminou. Entre de novo para excluir a conta.'],
    ['a 500 with no body', { status: 500, body: {} }, 'Não foi possível excluir a conta. Tente de novo.'],
  ])('shows %s and stays on the page', async (_, remove, message) => {
    stubApi({ remove });
    const { router } = renderSettings();

    await confirmDeletion();

    const zone = screen.getByRole('region', { name: 'Excluir minha conta' });
    expect(await within(zone).findByRole('alert')).toHaveTextContent(message);
    expect(router.state.location.pathname).toBe('/settings');
    expect(within(zone).getByRole('button', { name: 'Excluir definitivamente' })).toBeEnabled();
  });
});
