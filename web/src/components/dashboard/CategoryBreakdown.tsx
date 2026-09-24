import type { BreakdownKind } from '@/api/finance';
import Amount from '@/components/Amount';
import { Button } from '@/components/ui/button';
import { categoryKindPlurals } from '@/lib/labels';
import { formatMonth } from '@/lib/months';

import { useByCategory } from './queries';
import SectionHeading from './SectionHeading';

const KINDS: BreakdownKind[] = ['Expense', 'Income'];

const percent = (share: number) =>
  share.toLocaleString('pt-BR', { style: 'percent', minimumFractionDigits: 1, maximumFractionDigits: 1 });

/**
 * Section 4: the selected month by top-level category, as horizontal bars.
 *
 * Plain HTML bars rather than a chart library: a ranked list with a label, an amount
 * and a share is a table with a bar beside it, and as markup it reads the same to a
 * screen reader as it looks. Bar length is the row's share relative to the largest,
 * so the top category always fills the track; the API has already sorted the rows.
 */
export default function CategoryBreakdown({
  month,
  kind,
  onKind,
}: {
  month: string;
  kind: BreakdownKind;
  onKind: (kind: BreakdownKind) => void;
}) {
  const rows = useByCategory(month, kind);
  const largest = Math.max(0, ...(rows.data ?? []).map((row) => row.share));

  return (
    <section aria-labelledby="category-heading" data-testid="category-breakdown">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <SectionHeading id="category-heading">Por categoria · {formatMonth(month)}</SectionHeading>

        <div role="group" aria-label="Tipo" className="flex gap-1">
          {KINDS.map((option) => (
            <Button
              key={option}
              size="sm"
              variant={option === kind ? 'secondary' : 'ghost'}
              aria-pressed={option === kind}
              onClick={() => onKind(option)}
            >
              {categoryKindPlurals[option]}
            </Button>
          ))}
        </div>
      </div>

      {rows.isError ? (
        <p className="text-destructive mt-3 text-sm">Não foi possível carregar as categorias.</p>
      ) : rows.data?.length === 0 ? (
        <p className="text-muted-foreground mt-3 text-sm">Nada registrado neste mês.</p>
      ) : (
        <ul className="mt-3 grid gap-3">
          {(rows.data ?? []).map((row) => (
            <li key={row.categoryId} data-testid="category-row">
              <div className="flex items-baseline justify-between gap-4 text-sm">
                <span className="font-medium">{row.name}</span>
                <span className="flex gap-3">
                  <span className="text-muted-foreground tabular-nums">{percent(row.share)}</span>
                  <Amount value={row.amount} />
                </span>
              </div>
              <div className="bg-muted mt-1 h-2 overflow-hidden rounded-full">
                <div
                  className={kind === 'Expense' ? 'bg-chart-expense h-full rounded-full' : 'bg-chart-income h-full rounded-full'}
                  style={{ width: `${largest > 0 ? (row.share / largest) * 100 : 0}%` }}
                />
              </div>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
