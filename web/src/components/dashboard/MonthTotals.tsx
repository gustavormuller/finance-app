import { ChevronLeft, ChevronRight } from 'lucide-react';

import type { MonthSummary } from '@/api/finance';
import Amount from '@/components/Amount';
import { Button } from '@/components/ui/button';
import { formatMonth, shiftMonth } from '@/lib/months';

import SectionHeading from './SectionHeading';

/**
 * Section 2: income, expense and net for the selected month, with the selector that
 * also drives the category breakdown. Forward stops at the local current month —
 * there is nothing recorded in the future worth paging to.
 */
export default function MonthTotals({
  month,
  latest,
  totals,
  onMonth,
}: {
  month: string;
  latest: string;
  totals: MonthSummary;
  onMonth: (month: string) => void;
}) {
  return (
    <section aria-labelledby="month-heading">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <SectionHeading id="month-heading">Resumo do mês</SectionHeading>

        <div className="flex items-center gap-1">
          <Button variant="ghost" size="icon-sm" aria-label="Mês anterior" onClick={() => onMonth(shiftMonth(month, -1))}>
            <ChevronLeft />
          </Button>
          <span data-testid="selected-month" className="min-w-36 text-center text-sm font-medium">
            {formatMonth(month)}
          </span>
          <Button
            variant="ghost"
            size="icon-sm"
            aria-label="Próximo mês"
            disabled={month >= latest}
            onClick={() => onMonth(shiftMonth(month, 1))}
          >
            <ChevronRight />
          </Button>
        </div>
      </div>

      <dl className="mt-3 grid grid-cols-3 gap-3">
        <Stat label="Receitas" testId="month-income" value={totals.income} />
        <Stat label="Despesas" testId="month-expense" value={totals.expense} />
        <Stat label="Resultado" testId="month-net" value={totals.net} />
      </dl>
    </section>
  );
}

function Stat({ label, testId, value }: { label: string; testId: string; value: number }) {
  return (
    <div className="rounded-lg border p-3">
      <dt className="text-muted-foreground text-sm">{label}</dt>
      <dd data-testid={testId} className="mt-1 text-lg font-semibold">
        <Amount value={value} />
      </dd>
    </div>
  );
}
