import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import { api, type ProviderSyncSummary, type SyncRun } from '@/api/finance';
import Alert from '@/components/Alert';
import SectionHeading from '@/components/dashboard/SectionHeading';
import { Button } from '@/components/ui/button';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { syncProviderLabel, syncRunStatusLabels, syncTriggerLabels } from '@/lib/labels';
import { cn } from '@/lib/utils';

/** How often the list is read again while the newest run is still going. */
const POLL_MS = 2000;

const when = new Intl.DateTimeFormat('pt-BR', { dateStyle: 'short', timeStyle: 'short' });

function count(value: number, singular: string, plural: string) {
  return `${value.toLocaleString('pt-BR')} ${value === 1 ? singular : plural}`;
}

/**
 * The run history (spec 006 UI): when, trigger, status and what each provider did, and
 * the button that starts a manual run. The run happens after the API's 202, so the
 * list is polled while the newest run is `Running`. A refusal (the ten-minute limit)
 * is the API's pt-BR sentence, shown as it comes.
 */
export default function SyncRuns() {
  const queryClient = useQueryClient();

  const runs = useQuery({
    queryKey: ['sync-runs'],
    queryFn: api.listSyncRuns,
    // Only the newest: an older row left `Running` by a process that died is history,
    // not something to wait for.
    refetchInterval: (query) => (query.state.data?.[0]?.status === 'Running' ? POLL_MS : false),
  });

  const sync = useMutation({
    mutationFn: api.triggerSync,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['sync-runs'] }),
  });

  const running = runs.data?.[0]?.status === 'Running';

  return (
    <section aria-labelledby="sync-runs-heading" className="grid gap-4">
      <div className="flex flex-wrap items-center justify-between gap-4">
        <SectionHeading id="sync-runs-heading">Sincronizações</SectionHeading>
        <Button onClick={() => sync.mutate()} disabled={sync.isPending || running}>
          Sincronizar agora
        </Button>
      </div>

      {sync.isError && <Alert>{sync.error.message}</Alert>}
      {runs.isError && <Alert>Não foi possível carregar as sincronizações.</Alert>}

      {runs.data?.length === 0 ? (
        <p className="text-muted-foreground border-border border-t py-12 text-center text-sm">
          Nenhuma sincronização ainda.
        </p>
      ) : (
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Início</TableHead>
              <TableHead className="hidden sm:table-cell">Origem</TableHead>
              <TableHead>Situação</TableHead>
              <TableHead>Por provedor</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {(runs.data ?? []).map((run) => (
              <RunRow key={run.id} run={run} />
            ))}
          </TableBody>
        </Table>
      )}
    </section>
  );
}

function RunRow({ run }: { run: SyncRun }) {
  const providers = Object.entries(run.summary);

  return (
    <TableRow data-testid={`sync-run-${run.id}`} className="align-top">
      <TableCell className="tabular-nums whitespace-nowrap">{when.format(new Date(run.startedAt))}</TableCell>
      <TableCell className="hidden sm:table-cell">{syncTriggerLabels[run.trigger]}</TableCell>
      <TableCell
        data-testid="sync-run-status"
        className={cn(
          'font-medium whitespace-nowrap',
          run.status === 'Failed' && 'text-destructive',
          run.status === 'Running' && 'text-muted-foreground',
        )}
      >
        {syncRunStatusLabels[run.status]}
      </TableCell>
      <TableCell className="whitespace-normal">
        {providers.length === 0 ? (
          <span className="text-muted-foreground">—</span>
        ) : (
          <ul className="grid gap-2">
            {providers.map(([provider, summary]) => (
              <ProviderLine key={provider} provider={provider} summary={summary} />
            ))}
          </ul>
        )}
      </TableCell>
    </TableRow>
  );
}

function ProviderLine({ provider, summary }: { provider: string; summary: ProviderSyncSummary }) {
  const failures =
    summary.failures.length > 0 ? summary.failures : summary.error ? [{ item: '', error: summary.error }] : [];

  return (
    <li data-testid={`provider-summary-${provider}`}>
      <span className="font-medium">{syncProviderLabel(provider)}</span>
      <span className="text-muted-foreground">
        {' · '}
        {count(summary.rowsWritten, 'linha gravada', 'linhas gravadas')}
        {' · '}
        {count(summary.itemsSynced, 'item sincronizado', 'itens sincronizados')}
        {summary.itemsFailed > 0 && ` · ${summary.itemsFailed.toLocaleString('pt-BR')} com falha`}
      </span>
      {failures.length > 0 && (
        <ul className="text-destructive mt-1 grid gap-0.5 text-xs">
          {failures.map((failure, index) => (
            <li key={`${failure.item}-${index}`}>{failure.item ? `${failure.item}: ${failure.error}` : failure.error}</li>
          ))}
        </ul>
      )}
    </li>
  );
}
