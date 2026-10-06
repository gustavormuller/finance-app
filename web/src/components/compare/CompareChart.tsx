import { CartesianGrid, Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';

import type { Comparison } from '@/api/finance';
import SectionHeading from '@/components/dashboard/SectionHeading';
import { axisTick, shortDay, tooltipProps } from '@/lib/chart';
import { formatDate } from '@/lib/labels';
import { formatIndexTick } from '@/lib/rates';

import { lineColour, lineDash, type SeriesLabel } from './series';

const index = (value: number) => value.toLocaleString('pt-BR', { minimumFractionDigits: 2, maximumFractionDigits: 2 });

/** `2026-09-24` as `09/2026`, for an axis that spans more than a year. */
const monthYear = (isoDay: string) => formatDate(isoDay).slice(3);

/**
 * One line per drawn series, base 100 on the period's start, in the series' colour and dash.
 * Hovering lists every line on the date; the hidden table gives the same at each month's
 * last point. On a log axis equal ratios are equal heights, so BTC since 2014 and WEGE3 can
 * share one chart (decision 13).
 */
export default function CompareChart({ comparison, labels, log }: { comparison: Comparison; labels: SeriesLabel[]; log: boolean }) {
  const drawn = comparison.series.flatMap((series, position) => (series.change === null ? [] : [position]));
  const data = comparison.points.map((point) => ({
    date: point.date,
    ...Object.fromEntries(point.values.map((value, position) => [`s${position}`, value])),
  }));
  const long = (comparison.period?.days ?? 0) > 366;
  const monthEnds = comparison.points.filter((point, i) => comparison.points[i + 1]?.date.slice(0, 7) !== point.date.slice(0, 7));
  const title = comparison.period ? `Base 100 em ${formatDate(comparison.period.from)}` : 'Base 100';

  return (
    <section aria-labelledby="compare-chart-heading" data-testid="compare-chart" className="glass grid gap-3 rounded-2xl p-5 sm:p-6">
      <SectionHeading id="compare-chart-heading">{title}</SectionHeading>

      <div className="h-80 text-xs" aria-hidden="true">
        <ResponsiveContainer width="100%" height="100%" initialDimension={{ width: 640, height: 320 }}>
          <LineChart data={data} margin={{ top: 8, right: 8, bottom: 0, left: 0 }}>
            <CartesianGrid vertical={false} stroke="var(--border)" />
            <XAxis
              dataKey="date"
              tickFormatter={long ? monthYear : shortDay}
              tickLine={false}
              axisLine={false}
              minTickGap={32}
              tick={axisTick}
            />
            <YAxis
              scale={log ? 'log' : 'auto'}
              domain={['auto', 'auto']}
              allowDataOverflow={log}
              tickFormatter={formatIndexTick}
              tickLine={false}
              axisLine={false}
              width={56}
              tick={axisTick}
            />
            <Tooltip
              {...tooltipProps}
              cursor={{ stroke: 'var(--border)' }}
              labelFormatter={(date) => formatDate(String(date))}
              formatter={(value) => index(Number(value))}
            />
            {drawn.map((position) => (
              <Line
                key={comparison.series[position]!.key}
                type="linear"
                dataKey={`s${position}`}
                name={labels[position]?.label ?? ''}
                stroke={lineColour(position)}
                strokeDasharray={lineDash(position)}
                strokeWidth={2}
                dot={false}
                isAnimationActive={false}
              />
            ))}
          </LineChart>
        </ResponsiveContainer>
      </div>

      {/* A table does not shrink to sr-only's 1px, so the wrapper carries it. */}
      <div className="sr-only">
        <table>
          <caption>{title}, no último ponto de cada mês</caption>
          <thead>
            <tr>
              <th scope="col">Data</th>
              {drawn.map((position) => (
                <th key={position} scope="col">
                  {labels[position]?.label}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {monthEnds.map((point) => (
              <tr key={point.date}>
                <th scope="row">{formatDate(point.date)}</th>
                {drawn.map((position) => (
                  <td key={position}>{point.values[position] === null ? '' : index(point.values[position]!)}</td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  );
}
