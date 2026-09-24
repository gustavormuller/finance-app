import { CartesianGrid, Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';

import type { DailyRow } from '@/api/finance';
import SectionHeading from '@/components/dashboard/SectionHeading';
import { formatDate } from '@/lib/labels';
import { formatMoney } from '@/lib/money';

/** Axis ticks only: whole reais with pt-BR grouping, so the axis stays narrow. */
const tick = (value: number) => value.toLocaleString('pt-BR', { maximumFractionDigits: 0 });

/** `2026-09-24` as `24/09`, for the axis. */
const shortDay = (isoDay: string) => formatDate(isoDay).slice(0, 5);

/**
 * The asset's `ValueBrl` per day (spec 007 UI: a simple line, not 008's comparison).
 * One series, so no legend: the heading names it. Hovering gives the day's value; the
 * visually hidden table gives the value at each month's last row, since a row per
 * calendar day would be thousands of cells to a screen reader.
 */
export default function ValueChart({ rows }: { rows: DailyRow[] }) {
  const monthEnds = rows.filter((row, index) => rows[index + 1]?.date.slice(0, 7) !== row.date.slice(0, 7));

  return (
    <section aria-labelledby="value-heading" className="grid gap-3">
      <SectionHeading id="value-heading">Valor ao longo do tempo</SectionHeading>

      {rows.length === 0 ? (
        <p className="border-border text-muted-foreground border-t py-12 text-center text-sm">Sem histórico de valor ainda.</p>
      ) : (
        <div data-testid="value-chart">
          <div className="h-64 text-xs" aria-hidden="true">
            <ResponsiveContainer width="100%" height="100%">
              <LineChart data={rows} margin={{ top: 8, right: 8, bottom: 0, left: 0 }}>
                <CartesianGrid vertical={false} stroke="var(--border)" />
                <XAxis dataKey="date" tickFormatter={shortDay} tickLine={false} axisLine={false} minTickGap={32} />
                <YAxis tickFormatter={tick} tickLine={false} axisLine={false} width={64} domain={['auto', 'auto']} />
                <Tooltip
                  cursor={{ stroke: 'var(--border)' }}
                  labelFormatter={(date) => formatDate(String(date))}
                  formatter={(value) => [formatMoney(Number(value)), 'Valor']}
                  separator=": "
                  itemStyle={{ color: 'var(--foreground)' }}
                  contentStyle={{ background: 'var(--popover)', border: '1px solid var(--border)', borderRadius: 8 }}
                />
                <Line
                  type="monotone"
                  dataKey="valueBrl"
                  stroke="var(--chart-income)"
                  strokeWidth={2}
                  dot={false}
                  activeDot={{ r: 4, stroke: 'var(--background)', strokeWidth: 2 }}
                  isAnimationActive={false}
                />
              </LineChart>
            </ResponsiveContainer>
          </div>

          <table className="sr-only">
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
      )}
    </section>
  );
}
