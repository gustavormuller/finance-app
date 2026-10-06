import type { Comparison, CompareSeries } from '@/api/finance';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { currencySymbol, formatDate } from '@/lib/labels';
import { NO_DATA, formatRate, perYear, showsAnnualised } from '@/lib/rates';

import type { SeriesLabel } from './series';
import SeriesSwatch from './SeriesSwatch';

/** A series whose data stops this many days before the end says so; a weekend is not worth a note (decision 7). */
const STALE_AFTER_DAYS = 7;

const dayNumber = (isoDay: string) => {
  const [year, month, day] = isoDay.split('-').map(Number);

  return Date.UTC(year!, month! - 1, day!) / 86_400_000;
};

/** The currency a series is drawn in, as its row says it. */
function drawnIn(series: CompareSeries, currency: Comparison['currency']): string {
  if (currency === 'original' || series.currency === currency) {
    return currencySymbol(series.currency);
  }

  return `convertido para ${currencySymbol(currency)}`;
}

/**
 * The legend and the figures: each series' change over the period and, past a year, at a
 * year's rate (decision 11), in the order chosen. A series left out says why. On a phone the
 * year's rate sits under the change, so no column scrolls out of sight.
 */
export default function CompareTable({ comparison, labels }: { comparison: Comparison; labels: SeriesLabel[] }) {
  const period = comparison.period;
  const annualised = period !== null && showsAnnualised(period);

  return (
    <Table data-testid="compare-table">
      <TableHeader>
        <TableRow>
          <TableHead>Série</TableHead>
          <TableHead className="text-right">No período</TableHead>
          {annualised && <TableHead className="hidden text-right sm:table-cell">Ao ano</TableHead>}
        </TableRow>
      </TableHeader>
      <TableBody>
        {comparison.series.map((series, position) => {
          const label = labels[position];
          const stale =
            period !== null && series.change !== null && series.lastDate !== null && dayNumber(period.to) - dayNumber(series.lastDate) > STALE_AFTER_DAYS;

          return (
            <TableRow key={series.key}>
              <TableCell className="max-w-0 min-w-40 whitespace-normal sm:max-w-none">
                <div className="flex items-center gap-2">
                  <SeriesSwatch index={position} />
                  <span className="truncate font-semibold">{label?.label}</span>
                  <span className="text-muted-foreground shrink-0 text-xs">{drawnIn(series, comparison.currency)}</span>
                </div>
                {label?.detail && <p className="text-muted-foreground truncate pl-[26px] text-xs">{label.detail}</p>}
                {!series.hasData && period !== null && <p className="text-muted-foreground pl-[26px] text-xs">Sem dados no período</p>}
                {stale && <p className="text-muted-foreground pl-[26px] text-xs">dados até {formatDate(series.lastDate!)}</p>}
              </TableCell>
              <TableCell className="text-right align-top tabular-nums">
                {series.change === null ? NO_DATA : formatRate(series.change)}
                {annualised && series.change !== null && (
                  <span className="text-muted-foreground block text-xs sm:hidden">{perYear(series.annualised)}</span>
                )}
              </TableCell>
              {annualised && (
                <TableCell className="hidden text-right align-top tabular-nums sm:table-cell">
                  {series.change === null ? NO_DATA : perYear(series.annualised)}
                </TableCell>
              )}
            </TableRow>
          );
        })}
      </TableBody>
    </Table>
  );
}
