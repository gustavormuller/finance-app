import { Link } from '@tanstack/react-router';

import type { CommitResult, ImportBatch } from '@/api/finance';
import { Button } from '@/components/ui/button';

import UndoButton from './UndoButton';

/**
 * Step 4: what was written, what was not, and the two ways out — look at the rows,
 * or take them back.
 */
export default function DoneStep({
  batch,
  result,
  busy,
  onUndo,
  onNew,
}: {
  batch: ImportBatch;
  result: CommitResult;
  busy: boolean;
  onUndo: () => unknown;
  onNew: () => void;
}): React.JSX.Element {
  return (
    <div className="grid max-w-xl gap-6">
      <div className="rounded-lg border p-4">
        <p className="text-lg font-semibold" data-testid="commit-summary">
          {result.committed} {result.committed === 1 ? 'lançamento importado' : 'lançamentos importados'}
        </p>
        <p className="text-muted-foreground mt-1 text-sm">
          {result.skipped === 0
            ? 'Nenhuma linha ignorada.'
            : `${result.skipped} ${result.skipped === 1 ? 'linha ignorada' : 'linhas ignoradas'} (inválidas ou duplicadas não incluídas).`}
        </p>
        <p className="text-muted-foreground mt-1 text-sm">
          {batch.fileName} · {batch.accountName}
        </p>
      </div>

      <div className="flex flex-wrap items-center gap-2">
        <Button asChild>
          <Link to="/transactions" search={{ importBatchId: batch.id }}>
            Ver lançamentos
          </Link>
        </Button>
        <UndoButton count={result.committed} disabled={busy} onUndo={onUndo} />
        <Button type="button" variant="ghost" onClick={onNew}>
          Nova importação
        </Button>
      </div>
    </div>
  );
}
