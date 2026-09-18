import type {
  Category,
  ImportBatchDetail,
  StagedRow,
  StagedRowPatch,
  StagedRowStatus,
} from '@/api/finance';
import Alert from '@/components/Alert';
import Amount from '@/components/Amount';
import { Button } from '@/components/ui/button';
import { Label } from '@/components/ui/label';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { formatDate, stagedRowStatusLabels } from '@/lib/labels';

import { selectClasses } from './FileStep';

const STATUSES: StagedRowStatus[] = ['Ready', 'Duplicate', 'Invalid'];

/**
 * Step 3: every staged row with its status, its suggested category and, for a
 * duplicate, the checkbox that lets it in. Invalid rows show their reasons and
 * cannot be included; commit stays disabled while nothing would be written.
 */
export default function PreviewStep({
  detail,
  categories,
  filter,
  busy,
  error,
  onFilter,
  onPage,
  onPatchRow,
  onCommit,
  onDiscard,
}: {
  detail: ImportBatchDetail;
  categories: Category[];
  filter: StagedRowStatus | '';
  busy: boolean;
  error: React.ReactNode;
  onFilter: (filter: StagedRowStatus | '') => void;
  onPage: (page: number) => void;
  onPatchRow: (row: StagedRow, patch: StagedRowPatch) => void;
  onCommit: () => void;
  onDiscard: () => void;
}): React.JSX.Element {
  const { batch, counts, rows } = detail;
  const pages = Math.max(1, Math.ceil(rows.total / rows.pageSize));

  return (
    <div className="grid gap-6">
      <div className="flex flex-wrap items-center justify-between gap-4">
        <div>
          <p className="text-sm">
            <span className="font-medium">{batch.fileName}</span>
            <span className="text-muted-foreground"> · {batch.accountName}</span>
          </p>
          <p className="text-muted-foreground mt-1 text-sm" data-testid="preview-counts">
            {counts.ready} prontas · {counts.duplicates} duplicadas · {counts.invalid} inválidas ·{' '}
            <span className="text-foreground font-medium">{counts.included} a importar</span>
          </p>
        </div>

        <div className="grid gap-2">
          <Label htmlFor="preview-filter">Mostrar</Label>
          <select
            id="preview-filter"
            className={selectClasses}
            value={filter}
            onChange={(event) => onFilter(event.target.value as StagedRowStatus | '')}
          >
            <option value="">Todas as linhas</option>
            {STATUSES.map((status) => (
              <option key={status} value={status}>
                {stagedRowStatusLabels[status]}s
              </option>
            ))}
          </select>
        </div>
      </div>

      {error && <Alert>{error}</Alert>}

      <div className="overflow-x-auto rounded-md border">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead className="w-[60px]">#</TableHead>
              <TableHead className="w-[110px]">Data</TableHead>
              <TableHead>Descrição</TableHead>
              <TableHead className="w-[220px]">Categoria</TableHead>
              <TableHead className="text-right">Valor</TableHead>
              <TableHead>Situação</TableHead>
              <TableHead className="w-[90px]">Incluir</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {rows.items.map((row) => (
              <TableRow key={row.id} data-testid={`staged-row-${row.status}`}>
                <TableCell className="tabular-nums">{row.rowNumber}</TableCell>
                <TableCell className="tabular-nums">{row.date ? formatDate(row.date) : '—'}</TableCell>
                <TableCell>
                  <span className="block max-w-md truncate" title={row.rawDescription}>
                    {row.rawDescription || '—'}
                  </span>
                </TableCell>
                <TableCell>
                  <CategoryCell row={row} categories={categories} busy={busy} onPatchRow={onPatchRow} />
                </TableCell>
                <TableCell className="text-right">
                  {row.amount === null ? '—' : <Amount value={row.amount} />}
                </TableCell>
                <TableCell>
                  <span className="text-sm">{stagedRowStatusLabels[row.status]}</span>
                  {row.issues.length > 0 && (
                    <span className="text-destructive block text-xs">{row.issues.join(' · ')}</span>
                  )}
                </TableCell>
                <TableCell>
                  {row.status !== 'Invalid' && (
                    <input
                      type="checkbox"
                      aria-label={`Incluir linha ${row.rowNumber}`}
                      checked={row.included}
                      disabled={busy}
                      onChange={(event) => onPatchRow(row, { include: event.target.checked })}
                    />
                  )}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </div>

      {pages > 1 && (
        <div className="flex items-center justify-between">
          <p className="text-muted-foreground text-sm">
            Página {rows.page} de {pages} · {rows.total} linhas
          </p>
          <div className="flex gap-2">
            <Button variant="outline" size="sm" disabled={rows.page <= 1} onClick={() => onPage(rows.page - 1)}>
              Anterior
            </Button>
            <Button variant="outline" size="sm" disabled={rows.page >= pages} onClick={() => onPage(rows.page + 1)}>
              Próxima
            </Button>
          </div>
        </div>
      )}

      <div className="flex flex-wrap gap-2">
        <Button type="button" disabled={busy || counts.included === 0} onClick={onCommit}>
          Confirmar importação
        </Button>
        <Button type="button" variant="outline" disabled={busy} onClick={onDiscard}>
          Descartar
        </Button>
      </div>
    </div>
  );
}

/**
 * Only categories whose kind agrees with the row's sign are offered: the API
 * refuses the others (003, rule 3), so there is no point listing them.
 */
function CategoryCell({
  row,
  categories,
  busy,
  onPatchRow,
}: {
  row: StagedRow;
  categories: Category[];
  busy: boolean;
  onPatchRow: (row: StagedRow, patch: StagedRowPatch) => void;
}) {
  if (row.status === 'Invalid' || row.amount === null) {
    const name = categories.find((category) => category.id === row.categoryId)?.name;

    return <span className="text-muted-foreground text-sm">{name ?? '—'}</span>;
  }

  const kind = row.amount < 0 ? 'Expense' : 'Income';

  return (
    <select
      aria-label={`Categoria da linha ${row.rowNumber}`}
      className={selectClasses}
      value={row.categoryId ?? ''}
      disabled={busy}
      onChange={(event) => onPatchRow(row, { categoryId: event.target.value })}
    >
      {categories
        .filter((category) => category.kind === kind)
        .map((category) => (
          <option key={category.id} value={category.id}>
            {category.name}
          </option>
        ))}
    </select>
  );
}
