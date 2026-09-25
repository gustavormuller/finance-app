import { ChevronLeft, ChevronRight } from 'lucide-react';

import type { MonthSummary } from '@/api/finance';
import Amount from '@/components/Amount';
import Card from '@/components/Card';
import { Button } from '@/components/ui/button';
import { formatMonth, shiftMonth } from '@/lib/months';

import SectionHeading from './SectionHeading';

/**
 * Section 2: income, expense and net for the selected month, with the selector that
 * also drives the category breakdown. Forward stops at the local current month —
 * there is nothing recorded in the future worth paging to.
 *
 * 012: the net is the card's figure, with how much of the income it kept under it.
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
  const kept = totals.income > 0 ? Math.max(0, Math.min(1, totals.net / totals.income)) : 0;

  return (
    <Card aria-labelledby="month-heading" className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <SectionHeading id="month-heading">Resumo do mês</SectionHeading>

        <div className="flex items-center gap-1">
          <Button variant="ghost" size="icon-sm" aria-label="Mês anterior" onClick={() => onMonth(shiftMonth(month, -1))}>
            <ChevronLeft />
          </Button>
          <span data-testid="selected-month" className="min-w-32 text-center text-sm font-semibold">
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

      <dl className="grid gap-3">
        <Stat label="Receitas" testId="month-income" value={totals.income} />
        <Stat label="Despesas" testId="month-expense" value={totals.expense} />
        <div className="flex items-baseline justify-between gap-4 border-t pt-3">
          <dt className="text-muted-foreground text-sm">Resultado</dt>
          <dd data-testid="month-net" className="font-display text-3xl font-semibold">
            <Amount value={totals.net} />
          </dd>
        </div>
      </dl>

      {totals.income > 0 && (
        <div className="grid gap-2">
          <div className="bg-secondary h-2 overflow-hidden rounded-full">
            <div className="bg-chart-income h-full rounded-full" style={{ width: `${kept * 100}%` }} />
          </div>
          <p className="text-muted-foreground text-xs">
            {kept.toLocaleString('pt-BR', { style: 'percent', maximumFractionDigits: 0 })} do que entrou
          </p>
        </div>
      )}
    </Card>
  );
}

function Stat({ label, testId, value }: { label: string; testId: string; value: number }) {
  return (
    <div className="flex items-baseline justify-between gap-4">
      <dt className="text-muted-foreground text-sm">{label}</dt>
      <dd data-testid={testId} className="text-base font-semibold">
        <Amount value={value} />
      </dd>
    </div>
  );
}
