import { Plus } from 'lucide-react';
import type { ReactNode } from 'react';

import Amount from '@/components/Amount';
import { Button } from '@/components/ui/button';
import { TableCell, TableRow } from '@/components/ui/table';
import type { Use } from '@/lib/categoryTree';

/** Lançamentos, total and participação of one row; a null share (transfers) reads "—". */
export function UseCells({ use, share }: { use: Use; share: number | null }) {
  return (
    <>
      <TableCell className="text-muted-foreground text-right tabular-nums">
        <span data-testid="category-count">{use.count}</span>
      </TableCell>
      <TableCell className="text-right">
        {use.count > 0 ? <Amount value={use.total} /> : <span className="text-muted-foreground">—</span>}
      </TableCell>
      <TableCell className="hidden md:table-cell">
        {share === null ? (
          <span className="text-muted-foreground">—</span>
        ) : (
          <div data-testid="category-share" className="flex items-center gap-2">
            <span className="bg-secondary h-1.5 w-28 overflow-hidden rounded-full">
              <span className="bg-primary block h-full rounded-full" style={{ width: `${share * 100}%` }} />
            </span>
            <span className="text-muted-foreground w-12 text-right text-xs tabular-nums">
              {share.toLocaleString('pt-BR', { style: 'percent', minimumFractionDigits: 1, maximumFractionDigits: 1 })}
            </span>
          </div>
        )}
      </TableCell>
    </>
  );
}

/** The row's own actions; "Sub" only on main categories, since there are two levels. */
export function Actions({
  name,
  onSub,
  onEdit,
  onDelete,
}: {
  name: string;
  onSub?: () => void;
  onEdit: () => void;
  onDelete: () => void;
}) {
  return (
    <div className="flex justify-end gap-1">
      {onSub && (
        <Button variant="ghost" size="xs" aria-label={`Nova subcategoria em ${name}`} onClick={onSub}>
          <Plus aria-hidden="true" />
          Sub
        </Button>
      )}
      <Button variant="ghost" size="xs" aria-label={`Editar ${name}`} onClick={onEdit}>
        Editar
      </Button>
      <Button variant="ghost" size="xs" aria-label={`Excluir ${name}`} onClick={onDelete}>
        Excluir
      </Button>
    </div>
  );
}

/** A full-width row that holds the open form, right where the category is. */
export function FormRow({ children }: { children: ReactNode }) {
  return (
    <TableRow className="bg-accent hover:bg-accent">
      <TableCell colSpan={5} className="whitespace-normal">
        {children}
      </TableCell>
    </TableRow>
  );
}
