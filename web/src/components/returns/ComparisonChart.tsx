import { useState } from 'react';
import { CartesianGrid, Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';

import type { ReturnsPoint } from '@/api/finance';
import SectionHeading from '@/components/dashboard/SectionHeading';
import { Button } from '@/components/ui/button';
import { axisTick, tooltipProps } from '@/lib/chart';
import { benchmarkLabel, formatDate } from '@/lib/labels';
import { formatIndexTick } from '@/lib/rates';

const index = (value: number) => value.toLocaleString('pt-BR', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
const shortDay = (isoDay: string) => formatDate(isoDay).slice(0, 5);

/** Muted and told apart by dash as well as shade, so colour is never the only carrier. */
const DASHES = ['6 3', '2 3', '10 4', '4 2 1 2', '1 3'];

/**
 * Spec 008 "Comparison chart": base 100 on the base day, the portfolio's TWR index in
 * the primary colour, each benchmark muted and toggleable. `codes` are the benchmarks
 * that could anchor; a null one has no key in `series`. Hovering lists every visible
 * series on that date; the hidden table gives the same at each month's last point.
 */
export default function ComparisonChart({ series, codes }: { series: ReturnsPoint[]; codes: string[] }) {
  const [hidden, setHidden] = useState<string[]>([]);
  const shown = codes.filter((code) => !hidden.includes(code));
  const toggle = (code: string) => setHidden((current) => (current.includes(code) ? current.filter((c) => c !== code) : [...current, code]));
  const monthEnds = series.filter((point, i) => series[i + 1]?.date.slice(0, 7) !== point.date.slice(0, 7));

  return (
    <section aria-labelledby="comparison-heading" data-testid="comparison-chart" className="glass grid gap-3 rounded-2xl p-5 sm:p-6">
      <SectionHeading id="comparison-heading">Carteira e referências, base 100</SectionHeading>

      <div role="group" aria-label="Referências no gráfico" className="flex flex-wrap gap-1">
        {codes.map((code) => (
          <Button
            key={code}
            type="button"
            size="xs"
            variant={hidden.includes(code) ? 'ghost' : 'secondary'}
            aria-pressed={!hidden.includes(code)}
            onClick={() => toggle(code)}
          >
            {benchmarkLabel(code)}
          </Button>
        ))}
      </div>

      <div className="h-72 text-xs" aria-hidden="true">
        <ResponsiveContainer width="100%" height="100%" initialDimension={{ width: 640, height: 288 }}>
          <LineChart data={series} margin={{ top: 8, right: 8, bottom: 0, left: 0 }}>
            <CartesianGrid vertical={false} stroke="var(--border)" />
            <XAxis dataKey="date" tickFormatter={shortDay} tickLine={false} axisLine={false} minTickGap={32} tick={axisTick} />
            <YAxis tickFormatter={formatIndexTick} tickLine={false} axisLine={false} width={48} domain={['auto', 'auto']} tick={axisTick} />
            <Tooltip
              {...tooltipProps}
              cursor={{ stroke: 'var(--border)' }}
              labelFormatter={(date) => formatDate(String(date))}
              formatter={(value) => index(Number(value))}
            />
            {shown.map((code) => (
              <Line
                key={code}
                type="monotone"
                dataKey={code}
                name={benchmarkLabel(code)}
                stroke="var(--muted-foreground)"
                strokeDasharray={DASHES[codes.indexOf(code) % DASHES.length] ?? ''}
                strokeWidth={1.5}
                dot={false}
                isAnimationActive={false}
              />
            ))}
            <Line type="monotone" dataKey="portfolio" name="Carteira" stroke="var(--primary)" strokeWidth={2.5} dot={false} isAnimationActive={false} />
          </LineChart>
        </ResponsiveContainer>
      </div>

      {/* A table does not shrink to sr-only's 1px, so the wrapper carries it. */}
      <div className="sr-only">
        <table>
          <caption>Carteira e referências no último ponto de cada mês, base 100</caption>
          <thead>
            <tr>
              <th scope="col">Data</th>
              <th scope="col">Carteira</th>
              {shown.map((code) => (
                <th key={code} scope="col">{benchmarkLabel(code)}</th>
              ))}
            </tr>
          </thead>
          <tbody>
            {monthEnds.map((point) => (
              <tr key={point.date}>
                <th scope="row">{formatDate(point.date)}</th>
                <td>{index(point.portfolio)}</td>
                {shown.map((code) => (
                  <td key={code}>{typeof point[code] === 'number' ? index(point[code]) : ''}</td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  );
}
