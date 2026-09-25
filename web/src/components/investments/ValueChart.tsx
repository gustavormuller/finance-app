import { Area, AreaChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';

import type { DailyRow } from '@/api/finance';
import SectionHeading from '@/components/dashboard/SectionHeading';
import { axisTick, tooltipProps, wholeMoney } from '@/lib/chart';
import { formatDate } from '@/lib/labels';
import { formatMoney } from '@/lib/money';

/** `2026-09-24` as `24/09`, for the axis. */
const shortDay = (isoDay: string) => formatDate(isoDay).slice(0, 5);

/**
 * The asset's `ValueBrl` per day (spec 007 UI: a simple line, not 008's comparison;
 * 012 draws it over a fill that fades to the ground).
 * One series, so no legend: the heading names it. Hovering gives the day's value; the
 * visually hidden table gives the value at each month's last row, since a row per
 * calendar day would be thousands of cells to a screen reader.
 *
 * Always in reais: a dollar history needs a rate per day, which a BRL asset's rows do not
 * carry, so the heading says so when the page shows dollars (016, decision 16).
 */
export default function ValueChart({ rows, inReais = false }: { rows: DailyRow[]; inReais?: boolean }) {
  const monthEnds = rows.filter((row, index) => rows[index + 1]?.date.slice(0, 7) !== row.date.slice(0, 7));

  return (
    <section aria-labelledby="value-heading" className="glass grid gap-3 rounded-2xl p-5 sm:p-6">
      <SectionHeading id="value-heading">Valor ao longo do tempo{inReais && ', em reais'}</SectionHeading>

      {rows.length === 0 ? (
        <p className="text-muted-foreground py-12 text-center text-sm">Sem histórico de valor ainda.</p>
      ) : (
        <div data-testid="value-chart">
          <div className="h-64 text-xs" aria-hidden="true">
            <ResponsiveContainer width="100%" height="100%">
              <AreaChart data={rows} margin={{ top: 8, right: 8, bottom: 0, left: 0 }}>
                <defs>
                  <linearGradient id="fade-value" x1="0" y1="0" x2="0" y2="1">
                    <stop offset="0%" stopColor="var(--primary)" stopOpacity={0.3} />
                    <stop offset="100%" stopColor="var(--primary)" stopOpacity={0} />
                  </linearGradient>
                </defs>
                <CartesianGrid vertical={false} stroke="var(--border)" />
                <XAxis dataKey="date" tickFormatter={shortDay} tickLine={false} axisLine={false} minTickGap={32} tick={axisTick} />
                <YAxis tickFormatter={wholeMoney} tickLine={false} axisLine={false} width={64} domain={['auto', 'auto']} tick={axisTick} />
                <Tooltip
                  {...tooltipProps}
                  cursor={{ stroke: 'var(--border)' }}
                  labelFormatter={(date) => formatDate(String(date))}
                  formatter={(value) => [formatMoney(Number(value)), 'Valor']}
                />
                <Area
                  type="monotone"
                  dataKey="valueBrl"
                  stroke="var(--primary)"
                  strokeWidth={2}
                  fill="url(#fade-value)"
                  dot={false}
                  activeDot={{ r: 4, stroke: 'var(--background)', strokeWidth: 2 }}
                  isAnimationActive={false}
                />
              </AreaChart>
            </ResponsiveContainer>
          </div>

          {/* A table does not shrink to sr-only's 1px, so the wrapper carries it. */}
          <div className="sr-only">
            <table>
              <caption>Valor no último dia de cada mês</caption>
              <thead>
                <tr>
                  <th scope="col">Data</th>
                  <th scope="col">Valor</th>
                </tr>
              </thead>
              <tbody>
                {monthEnds.map((row) => (
                  <tr key={row.date}>
                    <th scope="row">{formatDate(row.date)}</th>
                    <td>{formatMoney(row.valueBrl)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </section>
  );
}
