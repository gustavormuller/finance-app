import type { PeriodReturn } from '@/api/finance';
import SectionHeading from '@/components/dashboard/SectionHeading';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { pointsDifference } from '@/lib/decimal';
import { benchmarkLabel } from '@/lib/labels';
import { NO_DATA, formatPoints, formatRate } from '@/lib/rates';

/**
 * Each benchmark's return beside the portfolio's, and the difference, portfolio minus
 * benchmark, in percentage points of the period's return. The only figure made here,
 * and it is made exactly (`pointsDifference`).
 */
export default function BenchmarksTable({
  twr,
  benchmarks,
  codes,
  annualised,
}: {
  twr: PeriodReturn;
  benchmarks: Record<string, PeriodReturn | null>;
  codes: string[];
  annualised: boolean;
}) {
  return (
    <section aria-labelledby="benchmarks-heading" className="grid gap-3">
      <SectionHeading id="benchmarks-heading">Comparação com referências</SectionHeading>

      <Table data-testid="benchmarks-table">
        <TableHeader>
          <TableRow>
            <TableHead>Referência</TableHead>
            <TableHead className="text-right">No período</TableHead>
            {annualised && <TableHead className="text-right">Ao ano</TableHead>}
            <TableHead className="text-right">Carteira menos referência</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          <TableRow data-testid="benchmark-row-portfolio" className="font-medium">
            <TableCell>Carteira</TableCell>
            <TableCell className="text-right tabular-nums">{formatRate(twr.total)}</TableCell>
            {annualised && <TableCell className="text-right tabular-nums">{formatRate(twr.annualised)}</TableCell>}
            <TableCell />
          </TableRow>
          {codes.map((code) => {
            const benchmark = benchmarks[code] ?? null;

            return (
              <TableRow key={code} data-testid={`benchmark-row-${code}`}>
                <TableCell>{benchmarkLabel(code)}</TableCell>
                <TableCell className="text-right tabular-nums">{formatRate(benchmark?.total)}</TableCell>
                {annualised && <TableCell className="text-right tabular-nums">{formatRate(benchmark?.annualised)}</TableCell>}
                <TableCell className="text-right tabular-nums">
                  {benchmark ? formatPoints(pointsDifference(twr.total, benchmark.total)) : NO_DATA}
                </TableCell>
              </TableRow>
            );
          })}
        </TableBody>
      </Table>
    </section>
  );
}
