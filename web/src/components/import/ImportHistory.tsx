import type { ImportBatch } from '@/api/finance';
import { Button } from '@/components/ui/button';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { importBatchStatusLabels, importSourceLabels } from '@/lib/labels';

import UndoButton from './UndoButton';

function formatInstant(iso: string): string {
  return new Date(iso).toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' });
}

/**
 * One account's past batches, newest first. A staged one can be resumed or
 * discarded; a committed one can be undone.
 */
export default function ImportHistory({
  batches,
  busy,
  onResume,
  onDiscard,
  onUndo,
}: {
  batches: ImportBatch[];
  busy: boolean;
  onResume: (batch: ImportBatch) => void;
  onDiscard: (batch: ImportBatch) => void;
  onUndo: (batch: ImportBatch) => unknown;
}): React.JSX.Element {
  if (batches.length === 0) {
    return <p className="text-muted-foreground py-4 text-sm">Nenhuma importação nesta conta ainda.</p>;
  }

  return (
    <div className="overflow-x-auto">
      <Table bare>
        <TableHeader>
          <TableRow>
            <TableHead>Arquivo</TableHead>
            <TableHead className="hidden sm:table-cell">Quando</TableHead>
            <TableHead className="hidden text-right sm:table-cell">Linhas</TableHead>
            <TableHead className="hidden text-right sm:table-cell">Importadas</TableHead>
            <TableHead />
          </TableRow>
        </TableHeader>
        <TableBody>
          {batches.map((batch) => (
            <TableRow key={batch.id} data-testid="import-history-row">
              {/* The file name wraps, and the status rides under it: the history sits
                  beside the account list, in half a page. On a phone the other columns
                  join that line, so the actions keep their room. */}
              <TableCell className="font-medium break-words whitespace-normal">
                {batch.fileName}
                <span className="text-muted-foreground block text-xs font-normal">
                  <span className="sm:hidden">{formatInstant(batch.createdAt)} · </span>
                  {importBatchStatusLabels[batch.status]} · {importSourceLabels[batch.source]}
                  <span className="sm:hidden">
                    {' '}
                    · {batch.rowCount} {batch.rowCount === 1 ? 'linha' : 'linhas'}
                    {batch.committedCount !== null && `, ${batch.committedCount} importadas`}
                  </span>
                </span>
              </TableCell>
              <TableCell className="text-muted-foreground hidden tabular-nums sm:table-cell">
                {formatInstant(batch.createdAt)}
              </TableCell>
              <TableCell className="hidden text-right tabular-nums sm:table-cell">{batch.rowCount}</TableCell>
              <TableCell className="hidden text-right tabular-nums sm:table-cell">
                {batch.committedCount ?? '—'}
              </TableCell>
              <TableCell>
                <div className="flex justify-end gap-1">
                  {batch.status === 'Staged' ? (
                    <>
                      <Button variant="ghost" size="sm" disabled={busy} onClick={() => onResume(batch)}>
                        Continuar
                      </Button>
                      <Button variant="ghost" size="sm" disabled={busy} onClick={() => onDiscard(batch)}>
                        Descartar
                      </Button>
                    </>
                  ) : (
                    <UndoButton
                      size="sm"
                      count={batch.committedCount ?? 0}
                      disabled={busy}
                      onUndo={() => onUndo(batch)}
                    />
                  )}
                </div>
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  );
}
