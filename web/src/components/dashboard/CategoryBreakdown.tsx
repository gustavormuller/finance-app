import type { BreakdownKind, CategoryTotal } from '@/api/finance';
import Amount from '@/components/Amount';
import Card from '@/components/Card';
import { Button } from '@/components/ui/button';
import { seriesColour } from '@/lib/chart';
import { categoryKindPlurals } from '@/lib/labels';
import { formatMonth } from '@/lib/months';

import { useByCategory } from './queries';
import SectionHeading from './SectionHeading';

const KINDS: BreakdownKind[] = ['Expense', 'Income'];

const percent = (share: number) =>
  share.toLocaleString('pt-BR', { style: 'percent', minimumFractionDigits: 1, maximumFractionDigits: 1 });

/**
 * Section 4: the selected month by top-level category.
 *
 * 012: a ring of the shares above a ranked list. The ring is decoration for the eye;
 * the list carries every figure — label, share and amount as markup — so it reads the
 * same to a screen reader, and each row's swatch matches its arc. The API has already
 * sorted the rows.
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

  return (
    <Card aria-labelledby="category-heading" data-testid="category-breakdown" className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <SectionHeading id="category-heading">Por categoria · {formatMonth(month)}</SectionHeading>

        <div role="group" aria-label="Tipo" className="bg-secondary flex gap-0.5 rounded-xl p-0.5">
          {KINDS.map((option) => (
            <Button
              key={option}
              size="xs"
              variant="ghost"
              aria-pressed={option === kind}
              onClick={() => onKind(option)}
              className="aria-pressed:bg-card aria-pressed:text-foreground text-muted-foreground aria-pressed:shadow-sm"
            >
              {categoryKindPlurals[option]}
            </Button>
          ))}
        </div>
      </div>

      {rows.isError ? (
        <p className="text-destructive text-sm">Não foi possível carregar as categorias.</p>
      ) : rows.data?.length === 0 ? (
        <p className="text-muted-foreground text-sm">Nada registrado neste mês.</p>
      ) : (
        <div className="flex flex-col items-center gap-5">
          <Ring rows={rows.data ?? []} />

          <ul className="grid w-full min-w-0 gap-2.5">
            {(rows.data ?? []).map((row, index) => (
              <li key={row.categoryId} data-testid="category-row" className="flex items-center gap-2.5 text-sm">
                <span aria-hidden="true" className="size-2.5 shrink-0 rounded-full" style={{ background: seriesColour(index) }} />
                <span className="min-w-0 flex-1 truncate font-medium">{row.name}</span>
                <span className="text-muted-foreground tabular-nums">{percent(row.share)}</span>
                <Amount value={row.amount} className="w-24 shrink-0" />
              </li>
            ))}
          </ul>
        </div>
      )}
    </Card>
  );
}

/** The shares as arcs with a small gap between them, largest first from twelve o'clock. */
function Ring({ rows }: { rows: CategoryTotal[] }) {
  const size = 132;
  const stroke = 14;
  const radius = (size - stroke) / 2;
  const circumference = 2 * Math.PI * radius;
  const gap = rows.length > 1 ? 4 : 0;
  let offset = 0;

  return (
    <svg width={size} height={size} viewBox={`0 0 ${size} ${size}`} aria-hidden="true" className="shrink-0">
      <circle cx={size / 2} cy={size / 2} r={radius} fill="none" stroke="var(--secondary)" strokeWidth={stroke} />
      {rows.map((row, index) => {
        const length = row.share * circumference;
        const dash = Math.max(length - gap, 0.5);
        const arc = (
          <circle
            key={row.categoryId}
            cx={size / 2}
            cy={size / 2}
            r={radius}
            fill="none"
            stroke={seriesColour(index)}
            strokeWidth={stroke}
            strokeDasharray={`${dash} ${circumference - dash}`}
            strokeDashoffset={-offset}
            transform={`rotate(-90 ${size / 2} ${size / 2})`}
          />
        );
        offset += length;
        return arc;
      })}
    </svg>
  );
}
