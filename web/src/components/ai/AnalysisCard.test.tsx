import { act, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';

import type { AiAnalysis, AnalysisStatus } from '@/api/finance';
import { renderWithClient, stubFetch, type SeenRequest } from '@/test-utils';

import AnalysisCard, { POLL_MS } from './AnalysisCard';

const CONTENT = '## Resumo\n\nVocê gastou **R$ 3.200,50** em setembro.\n\n## Sugestões\n\n- Revise as assinaturas';

function analysis(status: AnalysisStatus, extra: Partial<AiAnalysis> = {}): AiAnalysis {
  return {
    id: 'an-1',
    month: '2026-09',
    status,
    content: status === 'Completed' ? CONTENT : null,
    error: status === 'Failed' ? 'O serviço de IA não conseguiu responder agora. Tente novamente mais tarde.' : null,
    promptVersion: '1',
    createdAt: '2026-09-24T12:00:00Z',
    startedAt: null,
    completedAt: null,
    ...extra,
  };
}

/**
 * `list` answers each GET of the month's analyses in turn, the last one repeating;
 * `post` answers the POST.
 */
function stubApi({
  aiEnabled = true,
  list,
  post = { status: 202, body: { analysisId: 'an-1' } },
}: {
  aiEnabled?: boolean;
  list: AiAnalysis[][];
  post?: { status: number; body: unknown };
}) {
  let reads = 0;

  return stubFetch((request: SeenRequest) => {
    const path = new URL(request.url, 'http://localhost').pathname;

    if (path === '/api/auth/me') {
      return { body: { id: 'u1', email: 'ada@example.com', displayName: 'Ada', aiEnabled } };
    }

    if (path === '/api/ai/analyses' && request.method === 'POST') {
      return post;
    }

    if (path === '/api/ai/analyses') {
      return { body: list[Math.min(reads++, list.length - 1)] };
    }

    return undefined;
  });
}

const analysisReads = (seen: SeenRequest[]) =>
  seen.filter((request) => request.method === 'GET' && request.url.startsWith('/api/ai/analyses'));

describe('AnalysisCard', () => {
  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  /** Spec web unit test 26. */
  it(`polls every ${POLL_MS} ms while Pending or Running, and stops on Completed`, async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'setInterval', 'clearInterval'] });
    const seen = stubApi({ list: [[analysis('Pending')], [analysis('Running')], [analysis('Completed')]] });

    renderWithClient(<AnalysisCard month="2026-09" />);

    // A response body is read on the real event loop, which fake timers do not drive, so
    // what is on screen is waited for; the requests are counted at exact instants.
    const shows = (assertion: () => void) => act(() => vi.waitFor(assertion));

    await shows(() => expect(screen.getByTestId('analysis-progress')).toHaveTextContent('Na fila'));
    expect(analysisReads(seen).map((request) => request.url)).toEqual(['/api/ai/analyses?month=2026-09']);

    await act(() => vi.advanceTimersByTimeAsync(POLL_MS - 500));
    expect(analysisReads(seen)).toHaveLength(1);

    await act(() => vi.advanceTimersByTimeAsync(500));
    expect(analysisReads(seen)).toHaveLength(2);
    await shows(() => expect(screen.getByTestId('analysis-progress')).toHaveTextContent('Gerando'));

    await act(() => vi.advanceTimersByTimeAsync(POLL_MS));
    expect(analysisReads(seen)).toHaveLength(3);
    await shows(() =>
      expect(within(screen.getByTestId('analysis-content')).getByRole('heading', { name: 'Resumo' })).toBeInTheDocument(),
    );
    expect(screen.queryByTestId('analysis-progress')).not.toBeInTheDocument();

    await act(() => vi.advanceTimersByTimeAsync(POLL_MS * 5));
    expect(analysisReads(seen)).toHaveLength(3);
  });

  it('does not poll a Failed analysis, and shows its error verbatim with "Regenerar"', async () => {
    const seen = stubApi({ list: [[analysis('Failed')]] });
    renderWithClient(<AnalysisCard month="2026-09" />);

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'O serviço de IA não conseguiu responder agora. Tente novamente mais tarde.',
    );
    expect(screen.getByRole('button', { name: 'Regenerar' })).toBeEnabled();
    expect(analysisReads(seen)).toHaveLength(1);
  });

  /** Spec web unit test 27, on the card: the provider's content goes through the escaping renderer. */
  it('renders Completed markdown with any HTML in it as text', async () => {
    stubApi({ list: [[analysis('Completed', { content: '## Resumo\n\n<script>alert(1)</script> no mercado' })]] });
    renderWithClient(<AnalysisCard month="2026-09" />);

    const content = await screen.findByTestId('analysis-content');
    expect(within(content).getByRole('heading', { name: 'Resumo' })).toBeInTheDocument();
    expect(content.querySelector('script')).toBeNull();
    expect(content).toHaveTextContent('<script>alert(1)</script> no mercado');
  });

  it('offers "Gerar análise" when there is none, posts the month and polls the new row', async () => {
    const seen = stubApi({ list: [[], [analysis('Pending')]] });
    renderWithClient(<AnalysisCard month="2026-09" />);

    await userEvent.setup().click(await screen.findByRole('button', { name: 'Gerar análise' }));

    expect(await screen.findByTestId('analysis-progress')).toBeInTheDocument();
    expect(seen.filter((request) => request.method === 'POST')).toEqual([
      { method: 'POST', url: '/api/ai/analyses', body: { month: '2026-09' } },
    ]);
  });

  it('regenerates a Completed analysis with the same POST', async () => {
    const seen = stubApi({ list: [[analysis('Completed')], [analysis('Pending')]] });
    renderWithClient(<AnalysisCard month="2026-09" />);

    await userEvent.setup().click(await screen.findByRole('button', { name: 'Regenerar' }));

    expect(await screen.findByTestId('analysis-progress')).toBeInTheDocument();
    expect(screen.queryByTestId('analysis-content')).not.toBeInTheDocument();
    expect(seen.filter((request) => request.method === 'POST').map((request) => request.body)).toEqual([
      { month: '2026-09' },
    ]);
  });

  it('disables "Gerar análise" with the reason when AI is off', async () => {
    stubApi({ aiEnabled: false, list: [[]] });
    renderWithClient(<AnalysisCard month="2026-09" />);

    const generate = await screen.findByRole('button', { name: 'Gerar análise' });
    await vi.waitFor(() => expect(generate).toBeDisabled());
    expect(generate).toHaveAccessibleDescription(
      'A IA está desligada na sua conta. Ligue-a em Configurações para gerar a análise.',
    );
  });

  it('keeps a Completed analysis readable with AI off, and disables "Regenerar" with the reason', async () => {
    stubApi({ aiEnabled: false, list: [[analysis('Completed')]] });
    renderWithClient(<AnalysisCard month="2026-09" />);

    expect(await screen.findByTestId('analysis-content')).toBeInTheDocument();
    const regenerate = screen.getByRole('button', { name: 'Regenerar' });
    await vi.waitFor(() => expect(regenerate).toBeDisabled());
    expect(regenerate).toHaveAccessibleDescription(
      'A IA está desligada na sua conta. Ligue-a em Configurações para gerar a análise.',
    );
  });

  it.each([
    [403, 'A IA está desligada na sua conta. Ligue-a nas configurações para usar este recurso.'],
    [402, 'Você atingiu o limite mensal de gastos com IA. O limite renova no próximo mês.'],
  ])('renders a %i problem\'s detail verbatim', async (status, detail) => {
    stubApi({ list: [[]], post: { status, body: { status, title: 'Recusado', detail } } });
    renderWithClient(<AnalysisCard month="2026-09" />);

    await userEvent.setup().click(await screen.findByRole('button', { name: 'Gerar análise' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(detail);
    expect(screen.getByRole('button', { name: 'Gerar análise' })).toBeInTheDocument();
  });

  /** A 409 means a generation this card had not seen is running: say so, and pick it up. */
  it('renders a 409 verbatim and starts polling the analysis already in progress', async () => {
    const detail = 'A análise deste mês ainda está sendo gerada. Aguarde ela terminar para gerar outra.';
    stubApi({ list: [[], [analysis('Running')]], post: { status: 409, body: { status: 409, title: 'Conflito', detail } } });
    renderWithClient(<AnalysisCard month="2026-09" />);

    await userEvent.setup().click(await screen.findByRole('button', { name: 'Gerar análise' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(detail);
    expect(await screen.findByTestId('analysis-progress')).toHaveTextContent('Gerando');
  });
});
