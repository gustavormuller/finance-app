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
 * Past batches, newest first. A staged one can be resumed or discarded; a
 * committed one can be undone.
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
    return (
      <p className="text-muted-foreground border-t py-8 text-center text-sm">Nenhuma importação ainda.</p>
    );
  }

  return (
    <div className="overflow-x-auto rounded-md border">
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>Quando</TableHead>
            <TableHead>Arquivo</TableHead>
            <TableHead className="hidden md:table-cell">Conta</TableHead>
            <TableHead className="hidden md:table-cell">Situação</TableHead>
            <TableHead className="text-right">Linhas</TableHead>
            <TableHead className="text-right">Importadas</TableHead>
            <TableHead />
          </TableRow>
        </TableHeader>
        <TableBody>
          {batches.map((batch) => (
            <TableRow key={batch.id} data-testid="import-history-row">
              <TableCell className="whitespace-nowrap tabular-nums">{formatInstant(batch.createdAt)}</TableCell>
              <TableCell>
                {batch.fileName}
                <span className="text-muted-foreground block text-xs md:hidden">
                  {batch.accountName} · {importBatchStatusLabels[batch.status]}
                </span>
              </TableCell>
              <TableCell className="hidden md:table-cell">{batch.accountName}</TableCell>
              <TableCell className="hidden md:table-cell">
                {importBatchStatusLabels[batch.status]} · {importSourceLabels[batch.source]}
              </TableCell>
              <TableCell className="text-right tabular-nums">{batch.rowCount}</TableCell>
              <TableCell className="text-right tabular-nums">{batch.committedCount ?? '—'}</TableCell>
              <TableCell>
                <div className="flex flex-wrap justify-end gap-1">
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
