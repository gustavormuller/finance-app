import { Area, AreaChart, ReferenceDot, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';

import type { NetWorthPoint } from '@/api/finance';
import { money, tooltipProps } from '@/lib/chart';
import { formatMonth, shortMonth } from '@/lib/months';

/**
 * The hero's sparkline, net worth at each month's end. One series in the accent,
 * fading to the ground, with a dot on the latest point and only the first and last
 * months named; the hover tooltip gives any month's figure. The drawing is hidden from
 * screen readers, which get one sentence with the range and both ends instead.
 *
 * Nothing to draw with fewer than two points, so nothing is rendered.
 */
export default function NetWorthChart({ series }: { series: NetWorthPoint[] }) {
  const first = series[0];
  const last = series.at(-1);

  if (!first || !last || series.length < 2) {
    return null;
  }

  return (
    <div data-testid="net-worth-chart" className="flex flex-col gap-1.5">
      <div className="h-28 text-xs" aria-hidden="true">
        <ResponsiveContainer width="100%" height="100%">
          <AreaChart data={series} margin={{ top: 6, right: 6, bottom: 2, left: 6 }}>
            <defs>
              <linearGradient id="fade-net-worth" x1="0" y1="0" x2="0" y2="1">
                <stop offset="0%" stopColor="var(--primary)" stopOpacity={0.28} />
                <stop offset="100%" stopColor="var(--primary)" stopOpacity={0} />
              </linearGradient>
            </defs>
            <XAxis dataKey="month" hide />
            <YAxis hide domain={['dataMin', 'dataMax']} />
            <Tooltip
              {...tooltipProps}
              cursor={{ stroke: 'var(--border)' }}
              labelFormatter={(month) => formatMonth(String(month))}
              formatter={(value) => money(Number(value))}
            />
            <Area
              type="monotone"
              dataKey="total"
              name="Patrimônio"
              baseValue="dataMin"
              stroke="var(--primary)"
              strokeWidth={2}
              fill="url(#fade-net-worth)"
              // Drawn at once: the first thing on the page should not move, and motion is
              // kept to hover and focus.
              isAnimationActive={false}
              activeDot={{ r: 4, fill: 'var(--primary)', stroke: 'var(--popover)', strokeWidth: 2 }}
            />
            <ReferenceDot x={last.month} y={last.total} r={4.5} fill="var(--primary)" stroke="none" />
          </AreaChart>
        </ResponsiveContainer>
      </div>

      <div className="text-muted-foreground flex justify-between text-xs" aria-hidden="true">
        <span>{shortMonth(first.month)}</span>
        <span>{shortMonth(last.month)}</span>
      </div>

      <p className="sr-only">
        Patrimônio no fim de cada mês, de {formatMonth(first.month)} a {formatMonth(last.month)}: de R${' '}
        {money(first.total)} para R$ {money(last.total)}.
      </p>
    </div>
  );
}
