import type { PortfolioSummary } from '@/api/finance';
import Card from '@/components/Card';
import SectionHeading from '@/components/dashboard/SectionHeading';
import { seriesColour } from '@/lib/chart';
import { marketAssetClassLabels } from '@/lib/labels';
import { formatShare } from '@/lib/money';
import { cn } from '@/lib/utils';

import type { Display } from './useDisplayCurrency';

/** How each part of a large figure is drawn: the digits large, symbol and cents quieter. */
const PART_CLASSES: Partial<Record<Intl.NumberFormatPartTypes, string>> = {
  currency: 'text-muted-foreground text-base',
  literal: 'text-muted-foreground text-base',
  decimal: 'text-muted-foreground text-xl',
  fraction: 'text-muted-foreground text-xl',
};

function LargeMoney({ value, currency }: { value: number; currency: string }) {
  const parts = new Intl.NumberFormat('pt-BR', { style: 'currency', currency }).formatToParts(value);

  return parts.map((part, index) => (
    <span key={index} className={PART_CLASSES[part.type] ?? 'text-4xl'}>
      {part.value}
    </span>
  ));
}

const RESULT_TONES = {
  positive: 'bg-positive/10 text-positive',
  negative: 'bg-negative/10 text-negative',
  none: 'bg-secondary text-foreground',
};

/**
 * "Patrimônio investido" (016, design P1): the summary's total and its result over cost,
 * and the allocation by class as the API computed it, a stacked bar and a list. Colour
 * is never alone: every class is named beside its swatch.
 */
export default function Holdings({ summary, display }: { summary: PortfolioSummary; display: Display }) {
  const result = summary.unrealisedBrl;
  const tone = result > 0 ? RESULT_TONES.positive : result < 0 ? RESULT_TONES.negative : RESULT_TONES.none;

  return (
    <Card aria-labelledby="holdings-heading" data-testid="holdings" className="grid content-start gap-4">
      <SectionHeading id="holdings-heading">Patrimônio investido</SectionHeading>

      <p data-testid="holdings-total" className="font-display font-semibold tracking-tight break-words tabular-nums">
        <LargeMoney value={display.convert(summary.totalBrl)} currency={display.currency} />
      </p>
      <p data-testid="holdings-result" className={cn('w-fit rounded-full px-3 py-1 text-sm font-bold tabular-nums', tone)}>
        {display.signedMoney(result)} sobre o custo
      </p>

      {summary.allocation.length > 0 && (
        <>
          <div data-testid="stacked-allocation" className="flex h-2.5 gap-0.5 overflow-hidden rounded-full" aria-hidden="true">
            {summary.allocation.map((item, index) => (
              <span key={item.class} style={{ width: `${item.share * 100}%`, background: seriesColour(index) }} />
            ))}
          </div>
          <ul aria-label="Alocação por classe" className="grid gap-2">
            {summary.allocation.map((item, index) => (
              <li key={item.class} data-testid={`allocation-${item.class}`} className="flex items-center gap-2.5 text-sm">
                <span aria-hidden="true" className="size-2.5 shrink-0 rounded-[3px]" style={{ background: seriesColour(index) }} />
                <span className="min-w-0 flex-1 truncate">{marketAssetClassLabels[item.class]}</span>
                <span className="text-muted-foreground tabular-nums">{formatShare(item.share)}</span>
                <span className="w-24 text-right font-semibold tabular-nums">{display.compactMoney(item.valueBrl)}</span>
              </li>
            ))}
          </ul>
        </>
      )}
    </Card>
  );
}
