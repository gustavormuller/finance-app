import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';

import {
  ApiError,
  api,
  type CommitResult,
  type CsvMappingInput,
  type CsvPreview,
  type ImportBatch,
  type StagedRow,
  type StagedRowPatch,
  type StagedRowStatus,
} from '@/api/finance';
import DoneStep from '@/components/import/DoneStep';
import FileStep from '@/components/import/FileStep';
import ImportHistory from '@/components/import/ImportHistory';
import MappingStep from '@/components/import/MappingStep';
import PreviewStep from '@/components/import/PreviewStep';
import { Button } from '@/components/ui/button';

type Step =
  | { kind: 'file' }
  | { kind: 'mapping'; accountId: string; file: File; preview: CsvPreview; delimiter: string }
  | { kind: 'preview'; batchId: string }
  | { kind: 'done'; batch: ImportBatch; result: CommitResult };

const STEP_TITLES: Record<Step['kind'], string> = {
  file: '1. Arquivo',
  mapping: '2. Mapeamento',
  preview: '3. Revisão',
  done: '4. Concluído',
};

const ROWS_PER_PAGE = 100;

/** The sentence a refusal becomes, with the fields a 400 named appended. */
function describe(error: unknown): string {
  if (error instanceof ApiError) {
    const fields = Object.values(error.fields).flat();

    return fields.length > 0 ? fields.join(' ') : error.message;
  }

  return error instanceof Error ? error.message : 'Algo deu errado.';
}

/**
 * `/import`: the four-step flow on top, the history underneath. The step is a
 * discriminated union so the page cannot be in two steps at once, and every
 * transition is a plain assignment.
 */
export default function ImportPage(): React.JSX.Element {
  const queryClient = useQueryClient();
  const [step, setStep] = useState<Step>({ kind: 'file' });
  const [failure, setFailure] = useState<{ message: string; openBatchId: string | null } | null>(null);
  const [filter, setFilter] = useState<StagedRowStatus | ''>('');
  const [page, setPage] = useState(1);

  const accounts = useQuery({ queryKey: ['accounts'], queryFn: api.listAccounts });
  const categories = useQuery({ queryKey: ['categories'], queryFn: api.listCategories });
  const templates = useQuery({ queryKey: ['csv-templates'], queryFn: api.listCsvTemplates });
  const history = useQuery({ queryKey: ['imports'], queryFn: api.listImports });

  const batchId = step.kind === 'preview' ? step.batchId : null;
  const detail = useQuery({
    queryKey: ['imports', batchId, filter, page],
    queryFn: () => api.getImport(batchId!, { page, pageSize: ROWS_PER_PAGE, ...(filter ? { status: filter } : {}) }),
    enabled: batchId !== null,
    // A batch that was just discarded or committed answers 404; retrying that with
    // backoff would only keep the page busy.
    retry: false,
  });

  const fail = (error: unknown) =>
    setFailure({ message: describe(error), openBatchId: error instanceof ApiError ? error.openBatchId : null });

  const refresh = () => Promise.all([
    queryClient.invalidateQueries({ queryKey: ['imports'] }),
    queryClient.invalidateQueries({ queryKey: ['transactions'] }),
  ]);

  const openPreview = (id: string) => {
    setFailure(null);
    setFilter('');
    setPage(1);
    setStep({ kind: 'preview', batchId: id });
  };

  const start = useMutation({
    mutationFn: async ({ accountId, file }: { accountId: string; file: File }) => {
      // The extension decides the path: an OFX carries its own structure and goes
      // straight to staging; a CSV needs the user to say which column is what.
      if (file.name.toLowerCase().endsWith('.csv')) {
        const preview = await api.previewCsv(file);

        return { kind: 'mapping' as const, accountId, file, preview, delimiter: preview.delimiter };
      }

      const staged = await api.uploadImport({ file, accountId, source: 'Ofx' });

      return { kind: 'preview' as const, batchId: staged.batchId };
    },
    onSuccess: async (next) => {
      setFailure(null);
      await refresh();

      if (next.kind === 'preview') {
        openPreview(next.batchId);
      } else {
        setStep(next);
      }
    },
    onError: fail,
  });

  const rePreview = useMutation({
    mutationFn: async ({ file, delimiter }: { file: File; delimiter: string }) => ({
      preview: await api.previewCsv(file, delimiter),
      delimiter,
    }),
    onSuccess: ({ preview, delimiter }) =>
      setStep((current) => (current.kind === 'mapping' ? { ...current, preview, delimiter } : current)),
    onError: fail,
  });

  const uploadCsv = useMutation({
    mutationFn: async ({ mapping, saveAs }: { mapping: CsvMappingInput; saveAs: string | null }) => {
      if (step.kind !== 'mapping') {
        throw new Error('Nenhum arquivo em mapeamento.');
      }

      // The template is saved first, so a mapping worth keeping survives an upload
      // that is then refused for an unrelated reason.
      if (saveAs !== null) {
        await api.createCsvTemplate({ ...mapping, name: saveAs });
        await queryClient.invalidateQueries({ queryKey: ['csv-templates'] });
      }

      return api.uploadImport({ file: step.file, accountId: step.accountId, source: 'Csv', mapping });
    },
    onSuccess: async (staged) => {
      await refresh();
      openPreview(staged.batchId);
    },
    onError: fail,
  });

  const patchRow = useMutation({
    mutationFn: ({ row, patch }: { row: StagedRow; patch: StagedRowPatch }) =>
      api.patchImportRow(batchId!, row.id, patch),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['imports', batchId] }),
    onError: fail,
  });

  const commit = useMutation({
    mutationFn: async () => {
      const current = detail.data!.batch;
      const result = await api.commitImport(current.id);

      return { batch: { ...current, status: 'Committed' as const, committedCount: result.committed }, result };
    },
    onSuccess: async (done) => {
      setFailure(null);
      // The step changes before the refresh, so the detail query is disabled rather
      // than refetched against a batch whose rows are gone.
      setStep({ kind: 'done', ...done });
      await refresh();
    },
    onError: fail,
  });

  const discard = useMutation({
    mutationFn: (id: string) => api.discardImport(id),
    onSuccess: async () => {
      setFailure(null);
      setStep({ kind: 'file' });
      await refresh();
    },
    onError: fail,
  });

  const undo = useMutation({
    mutationFn: (id: string) => api.undoImport(id),
    onSuccess: async () => {
      setFailure(null);
      setStep({ kind: 'file' });
      await refresh();
    },
    onError: fail,
  });

  const busy =
    start.isPending ||
    rePreview.isPending ||
    uploadCsv.isPending ||
    patchRow.isPending ||
    commit.isPending ||
    discard.isPending ||
    undo.isPending;

  const error = failure && (
    <>
      {failure.message}
      {failure.openBatchId && (
        <>
          {' '}
          <button type="button" className="underline" onClick={() => openPreview(failure.openBatchId!)}>
            Abrir a importação em andamento
          </button>
        </>
      )}
    </>
  );

  return (
    <section className="mx-auto max-w-5xl px-4 py-8">
      <div className="mb-6 flex items-center justify-between gap-4">
        <h2 className="text-2xl font-semibold tracking-tight">Importar</h2>
        {step.kind !== 'file' && (
          <Button variant="ghost" size="sm" onClick={() => { setFailure(null); setStep({ kind: 'file' }); }}>
            Recomeçar
          </Button>
        )}
      </div>

      <h3 className="text-muted-foreground mb-4 text-sm font-medium tracking-wide uppercase">
        {STEP_TITLES[step.kind]}
      </h3>

      {/* One container for the current step, so its buttons are distinguishable from
          the history's, which offers the same verbs for other batches. */}
      <div data-testid="import-step">
      {step.kind === 'file' && (
        <FileStep
          accounts={accounts.data ?? []}
          busy={busy}
          error={error}
          onSubmit={(accountId, file) => start.mutate({ accountId, file })}
        />
      )}

      {step.kind === 'mapping' && (
        <MappingStep
          preview={step.preview}
          templates={templates.data ?? []}
          delimiter={step.delimiter}
          busy={busy}
          error={error}
          onDelimiterChange={(delimiter) => rePreview.mutate({ file: step.file, delimiter })}
          onBack={() => { setFailure(null); setStep({ kind: 'file' }); }}
          onSubmit={(mapping, saveAs) => uploadCsv.mutate({ mapping, saveAs })}
        />
      )}

      {step.kind === 'preview' && detail.data && (
        <PreviewStep
          detail={detail.data}
          categories={categories.data ?? []}
          filter={filter}
          busy={busy}
          error={error}
          onFilter={(next) => { setFilter(next); setPage(1); }}
          onPage={setPage}
          onPatchRow={(row, patch) => patchRow.mutate({ row, patch })}
          onCommit={() => commit.mutate()}
          onDiscard={() => discard.mutate(step.batchId)}
        />
      )}

      {step.kind === 'done' && (
        <DoneStep
          batch={step.batch}
          result={step.result}
          busy={busy}
          onUndo={() => undo.mutateAsync(step.batch.id)}
          onNew={() => setStep({ kind: 'file' })}
        />
      )}
      </div>

      <div className="mt-12">
        <h3 className="mb-3 text-base font-semibold">Histórico</h3>
        <ImportHistory
          batches={history.data ?? []}
          busy={busy}
          onResume={(batch) => openPreview(batch.id)}
          onDiscard={(batch) => discard.mutate(batch.id)}
          onUndo={(batch) => undo.mutateAsync(batch.id)}
        />
      </div>
    </section>
  );
}
