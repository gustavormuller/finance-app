import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';
import { useState } from 'react';

import {
  ApiError,
  api,
  type Account,
  type CommitResult,
  type CsvMappingInput,
  type CsvPreview,
  type ImportBatch,
  type ImportBatchDetail,
  type StagedRow,
  type StagedRowPatch,
  type StagedRowStatus,
  type SuggestResult,
} from '@/api/finance';
import { useMe } from '@/auth/useMe';
import { useImports } from '@/components/accounts/queries';
import { Button } from '@/components/ui/button';

import DoneStep from './DoneStep';
import FileStep from './FileStep';
import ImportHistory from './ImportHistory';
import MappingStep from './MappingStep';
import PreviewStep from './PreviewStep';

type Step =
  | { kind: 'file' }
  | { kind: 'mapping'; file: File; preview: CsvPreview; delimiter: string | null }
  | { kind: 'preview'; batchId: string }
  | { kind: 'done'; batch: ImportBatch; result: CommitResult };

const STEP_TITLES: Record<Step['kind'], string> = {
  file: '1. Arquivo',
  mapping: '2. Mapeamento',
  preview: '3. Revisão',
  done: '4. Concluído',
};

const ROWS_PER_PAGE = 100;

/** What a suggestion did, in a sentence: the rows the AI moved, and those it left. */
function suggestionNotice({ suggested, skipped }: SuggestResult): string {
  if (suggested === 0 && skipped === 0) {
    return 'Nenhuma linha na categoria padrão para a IA sugerir.';
  }

  const moved =
    suggested === 0
      ? 'A IA não sugeriu nenhuma categoria.'
      : suggested === 1
        ? '1 categoria sugerida pela IA.'
        : `${suggested} categorias sugeridas pela IA.`;
  const left =
    skipped === 0
      ? ''
      : skipped === 1
        ? ' 1 linha continuou na categoria padrão.'
        : ` ${skipped} linhas continuaram na categoria padrão.`;

  return moved + left;
}

/** The sentence a refusal becomes, with the fields a 400 named appended. */
function describe(error: unknown): string {
  if (error instanceof ApiError) {
    const fields = Object.values(error.fields).flat();

    return fields.length > 0 ? fields.join(' ') : error.message;
  }

  return error instanceof Error ? error.message : 'Algo deu errado.';
}

/**
 * "Importar extrato": the four steps on top, this
 * account's history underneath. The account is the page's, so no step asks for one.
 * The step is a discriminated union so the tab cannot be in two steps at once, and
 * every transition is a plain assignment.
 */
export default function AccountImport({ account }: { account: Account }): React.JSX.Element {
  const queryClient = useQueryClient();
  const [step, setStep] = useState<Step>({ kind: 'file' });
  const [failure, setFailure] = useState<{ message: string; openBatchId: string | null } | null>(null);
  const [filter, setFilter] = useState<StagedRowStatus | ''>('');
  const [page, setPage] = useState(1);
  const [notice, setNotice] = useState<string | null>(null);

  const me = useMe();

  const categories = useQuery({ queryKey: ['categories'], queryFn: api.listCategories });
  const templates = useQuery({ queryKey: ['csv-templates'], queryFn: api.listCsvTemplates });
  const history = useImports();
  const batches = (history.data ?? []).filter((batch) => batch.accountId === account.id);
  // The API keeps one staged batch per user, on whichever account it is.
  const inReview = history.data?.find((batch) => batch.status === 'Staged') ?? null;

  const batchId = step.kind === 'preview' ? step.batchId : null;
  const detail = useQuery({
    queryKey: ['imports', batchId, filter, page],
    queryFn: () => api.getImport(batchId!, { page, pageSize: ROWS_PER_PAGE, ...(filter ? { status: filter } : {}) }),
    enabled: batchId !== null,
    // A batch that was just discarded or committed answers 404; retrying that with
    // backoff would only keep the page busy.
    retry: false,
  });

  const fail = (error: unknown) => {
    const openBatchId = error instanceof ApiError ? error.openBatchId : null;

    setFailure({ message: describe(error), openBatchId });

    // A batch opened elsewhere (another tab) is news to the history, and to the notice
    // that replaces the drop zone while it is open.
    if (openBatchId) {
      void queryClient.invalidateQueries({ queryKey: ['imports'], exact: true });
    }
  };

  /** A batch that has just stopped being staged, out of the history before it is refetched. */
  const forget = (id: string) =>
    queryClient.setQueryData<ImportBatch[]>(['imports'], (current) => current?.filter((each) => each.id !== id));

  // The dashboard's summary too: the account list shows its balances.
  const refresh = () => Promise.all([
    queryClient.invalidateQueries({ queryKey: ['imports'] }),
    queryClient.invalidateQueries({ queryKey: ['transactions'] }),
    queryClient.invalidateQueries({ queryKey: ['dashboard'] }),
  ]);

  const openPreview = (id: string) => {
    setFailure(null);
    setNotice(null);
    setFilter('');
    setPage(1);
    setStep({ kind: 'preview', batchId: id });
  };

  const start = useMutation({
    mutationFn: async (file: File) => {
      // The extension decides the path: an OFX carries its own structure and goes
      // straight to staging; a CSV or a spreadsheet needs the user to say which
      // column is what. A spreadsheet's preview has no delimiter.
      if (/\.(csv|xlsx?)$/i.test(file.name)) {
        const preview = await api.previewCsv(file);

        return { kind: 'mapping' as const, file, preview, delimiter: preview.delimiter };
      }

      const staged = await api.uploadImport({ file, accountId: account.id, source: 'Ofx' });

      return { kind: 'preview' as const, batchId: staged.batchId };
    },
    onSuccess: async (next) => {
      setFailure(null);

      // The step first: refreshed while still on the file step, the history's new
      // staged batch would show the "in review" notice for a moment.
      if (next.kind === 'preview') {
        openPreview(next.batchId);
      } else {
        setStep(next);
      }

      await refresh();
    },
    onError: fail,
  });

  const rePreview = useMutation({
    mutationFn: async ({
      file,
      delimiter,
      culture,
      dateFormat,
    }: {
      file: File;
      delimiter: string | null;
      culture?: string;
      dateFormat?: string;
    }) => ({
      preview: await api.previewCsv(file, { delimiter: delimiter ?? undefined, culture, dateFormat }),
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

      const source = step.delimiter === null ? 'Spreadsheet' : 'Csv';

      return api.uploadImport({ file: step.file, accountId: account.id, source, mapping });
    },
    onSuccess: async (staged) => {
      await refresh();
      openPreview(staged.batchId);
    },
    onError: fail,
  });

  const detailKey = ['imports', batchId, filter, page];

  const patchRow = useMutation({
    mutationFn: ({ row, patch }: { row: StagedRow; patch: StagedRowPatch }) =>
      api.patchImportRow(batchId!, row.id, patch),
    // Optimistic: a checkbox that only ticks after a round trip reads as broken.
    // The refetch after success puts the server's truth back either way.
    onMutate: ({ row, patch }) =>
      queryClient.setQueryData<ImportBatchDetail>(detailKey, (current) => {
        if (!current) {
          return current;
        }

        const included = patch.include ?? row.included;

        return {
          ...current,
          counts: {
            ...current.counts,
            included: current.counts.included + Number(included) - Number(row.included),
          },
          rows: {
            ...current.rows,
            items: current.rows.items.map((item) =>
              item.id === row.id
                ? {
                    ...item,
                    included,
                    categoryId: patch.categoryId ?? item.categoryId,
                    // A category picked by hand is the user's, which drops the AI marker.
                    categorySource: patch.categoryId ? 'User' : item.categorySource,
                  }
                : item,
            ),
          },
        };
      }),
    onSettled: () => queryClient.invalidateQueries({ queryKey: ['imports', batchId] }),
    onError: fail,
  });

  // Rung 3 of the cascade, on the rows the sign default filed. The rows are refetched,
  // not patched from the answer, which carries only counts.
  const suggest = useMutation({
    mutationFn: () => api.suggestCategories(batchId!),
    onMutate: () => {
      setFailure(null);
      setNotice(null);
    },
    onSuccess: (result) => setNotice(suggestionNotice(result)),
    onSettled: () => Promise.all([
      queryClient.invalidateQueries({ queryKey: ['imports', batchId] }),
      queryClient.invalidateQueries({ queryKey: ['ai', 'usage'] }),
    ]),
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

  // Both remove the batch, so it leaves the history before the file step shows: still
  // listed as staged, it would put the "in review" notice where the drop zone belongs.
  const discard = useMutation({
    mutationFn: (id: string) => api.discardImport(id),
    onSuccess: async (_, id) => {
      setFailure(null);
      forget(id);
      setStep({ kind: 'file' });
      await refresh();
    },
    onError: fail,
  });

  const undo = useMutation({
    mutationFn: (id: string) => api.undoImport(id),
    onSuccess: async (_, id) => {
      setFailure(null);
      forget(id);
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
    suggest.isPending ||
    commit.isPending ||
    discard.isPending ||
    undo.isPending;

  // A 409 names the open batch; one on another account is opened there, not here.
  const elsewhere = failure?.openBatchId
    ? history.data?.find((batch) => batch.id === failure.openBatchId && batch.accountId !== account.id)
    : undefined;

  const error = failure && (
    <>
      {failure.message}
      {elsewhere ? (
        <>
          {' '}
          <InReviewLink batch={elsewhere} />
        </>
      ) : (
        failure.openBatchId && (
          <>
            {' '}
            <button type="button" className="underline" onClick={() => openPreview(failure.openBatchId!)}>
              Abrir a importação em andamento
            </button>
          </>
        )
      )}
    </>
  );

  return (
    <div className="grid gap-6 [&>*]:min-w-0">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h4 className="text-muted-foreground font-sans text-sm font-semibold">{STEP_TITLES[step.kind]}</h4>
        {step.kind !== 'file' && (
          <Button variant="ghost" size="sm" onClick={() => { setFailure(null); setStep({ kind: 'file' }); }}>
            Recomeçar
          </Button>
        )}
      </div>

      {/* One container for the current step, so its buttons are distinguishable from
          the history's, which offers the same verbs for other batches. `data-wide` asks
          the accounts page for the whole width while a table is the work. */}
      <div
        data-testid="import-step"
        data-wide={step.kind === 'mapping' || step.kind === 'preview' ? '' : undefined}
        className="-mt-3"
      >
      {step.kind === 'file' &&
        (inReview && inReview.accountId !== account.id ? (
          <p className="bg-accent grid gap-2 rounded-2xl p-4 text-sm">
            <span>
              Há um extrato em revisão na conta {inReview.accountName}. Conclua ou descarte essa importação antes de
              importar outro.
            </span>
            <InReviewLink batch={inReview} />
          </p>
        ) : inReview ? (
          <div className="bg-accent flex flex-wrap items-center justify-between gap-3 rounded-2xl p-4 text-sm">
            <p>
              <span>O extrato {inReview.fileName} ainda está em revisão.</span>{' '}
              <span className="text-muted-foreground">Confirme ou descarte-o antes de enviar outro.</span>
            </p>
            <Button onClick={() => openPreview(inReview.id)}>Continuar a revisão</Button>
          </div>
        ) : (
          <FileStep accountName={account.name} busy={busy} error={error} onFile={(file) => start.mutate(file)} />
        ))}

      {step.kind === 'mapping' && (
        <MappingStep
          preview={step.preview}
          templates={templates.data ?? []}
          delimiter={step.delimiter}
          busy={busy}
          error={error}
          onDelimiterChange={(delimiter) => rePreview.mutate({ file: step.file, delimiter })}
          onFormatChange={
            step.delimiter === null
              ? (culture, dateFormat) => rePreview.mutate({ file: step.file, delimiter: null, culture, dateFormat })
              : undefined
          }
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
          // A call can take up to 30 s per batch of rows, so the wait is said out loud.
          notice={suggest.isPending ? 'Pedindo sugestões à IA…' : notice}
          aiEnabled={me.data?.aiEnabled ?? false}
          onSuggest={() => suggest.mutate()}
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

      <div className="grid gap-3">
        <h4 className="text-base font-semibold">Importações desta conta</h4>
        <ImportHistory
          batches={batches}
          busy={busy}
          onResume={(batch) => openPreview(batch.id)}
          onDiscard={(batch) => discard.mutate(batch.id)}
          onUndo={(batch) => undo.mutateAsync(batch.id)}
        />
      </div>
    </div>
  );
}

/** To the import tab of the account whose statement is in review. */
function InReviewLink({ batch }: { batch: ImportBatch }) {
  return (
    <Link
      to="/accounts/$accountId"
      params={{ accountId: batch.accountId }}
      search={{ tab: 'import' }}
      className="text-primary font-semibold underline-offset-4 hover:underline"
    >
      Abrir a importação da conta {batch.accountName}
    </Link>
  );
}
