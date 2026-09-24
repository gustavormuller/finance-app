import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Loader2 } from 'lucide-react';

import { ApiError, api, type AiAnalysis } from '@/api/finance';
import { useMe } from '@/auth/useMe';
import Alert from '@/components/Alert';
import SectionHeading from '@/components/dashboard/SectionHeading';
import { Button } from '@/components/ui/button';
import { analysisStatusLabels } from '@/lib/labels';
import { formatMonth } from '@/lib/months';

import Markdown from './Markdown';

/** Spec 009: "a spinner and polling every 3 s" while the analysis is Pending or Running. */
export const POLL_MS = 3000;

const inProgress = (analysis: AiAnalysis | undefined) =>
  analysis?.status === 'Pending' || analysis?.status === 'Running';

/** The sentence a refused POST becomes: a 400's field messages, or the problem's detail. */
function describe(error: Error): string {
  const fields = error instanceof ApiError ? Object.values(error.fields).flat() : [];

  return fields.length > 0 ? fields.join(' ') : error.message;
}

/**
 * The dashboard's "Análise do mês" (009) for the selected month.
 *
 * The month's row is read from `GET /api/ai/analyses?month=`, which is also what is
 * polled: it answers the same shape as the by-id read, and it still finds the row when
 * a regenerate or another tab has just reset it. Polling stops by itself once the row
 * settles, because the interval is decided from the answer.
 */
export default function AnalysisCard({ month }: { month: string }) {
  const queryClient = useQueryClient();
  const me = useMe();
  const aiEnabled = me.data?.aiEnabled ?? false;
  const key = ['ai', 'analyses', month];

  const analyses = useQuery({
    queryKey: key,
    queryFn: () => api.listAnalyses(month),
    refetchInterval: (query) => (inProgress(query.state.data?.[0]) ? POLL_MS : false),
  });

  const generate = useMutation({
    mutationFn: () => api.requestAnalysis(month),
    // A 409 means a generation is already running for this month, perhaps from another
    // tab: refetching picks it up and starts the polling, as a 202 does.
    onSettled: (_, error) =>
      Promise.all([
        !error || (error instanceof ApiError && error.status === 409)
          ? queryClient.invalidateQueries({ queryKey: key })
          : undefined,
        queryClient.invalidateQueries({ queryKey: ['ai', 'usage'] }),
      ]),
  });

  const analysis = analyses.data?.[0];
  const busy = generate.isPending || inProgress(analysis);

  const action = (label: string) => (
    <div className="flex flex-wrap items-center gap-3">
      <Button
        type="button"
        variant={analysis ? 'outline' : 'default'}
        size="sm"
        disabled={!aiEnabled || busy}
        aria-describedby={aiEnabled ? undefined : 'analysis-disabled-reason'}
        onClick={() => generate.mutate()}
      >
        {label}
      </Button>
      {!aiEnabled && (
        <p id="analysis-disabled-reason" className="text-muted-foreground text-sm">
          A IA está desligada na sua conta. Ligue-a em Configurações para gerar a análise.
        </p>
      )}
    </div>
  );

  return (
    <section aria-labelledby="analysis-heading" data-testid="analysis-card" className="grid gap-3">
      <SectionHeading id="analysis-heading">Análise do mês</SectionHeading>

      {generate.isError && <Alert>{describe(generate.error)}</Alert>}

      {analyses.isError ? (
        <Alert>Não foi possível carregar a análise do mês.</Alert>
      ) : !analyses.data ? (
        <p className="text-muted-foreground text-sm">Carregando…</p>
      ) : !analysis ? (
        <div className="grid gap-3 rounded-lg border p-4">
          <p className="text-muted-foreground text-sm">
            Nenhuma análise de {formatMonth(month)} ainda. A IA lê os totais do mês e escreve um resumo com
            sugestões.
          </p>
          {action('Gerar análise')}
        </div>
      ) : inProgress(analysis) ? (
        <div
          role="status"
          data-testid="analysis-progress"
          className="text-muted-foreground flex items-center gap-2 rounded-lg border p-4 text-sm"
        >
          <Loader2 aria-hidden className="size-4 animate-spin" />
          {analysisStatusLabels[analysis.status]}… a análise de {formatMonth(month)} aparece aqui assim que ficar
          pronta.
        </div>
      ) : analysis.status === 'Failed' ? (
        <div className="grid gap-3 rounded-lg border p-4">
          <Alert>{analysis.error}</Alert>
          {action('Regenerar')}
        </div>
      ) : (
        <div className="grid gap-4 rounded-lg border p-4">
          <div data-testid="analysis-content">
            <Markdown source={analysis.content ?? ''} />
          </div>
          <p className="text-muted-foreground text-xs">
            Texto gerado por IA a partir dos seus totais. Confira os números antes de tomar uma decisão.
          </p>
          {action('Regenerar')}
        </div>
      )}
    </section>
  );
}
