import { Area, AreaChart, CartesianGrid, Legend, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';

import type { MonthTotals } from '@/api/finance';
import Card from '@/components/Card';
import { axisTick, money, tooltipProps, wholeMoney } from '@/lib/chart';
import { formatMonth, shortMonth } from '@/lib/months';

import SectionHeading from './SectionHeading';

/**
 * Section 3: income and expense per month. Expense arrives negative and is drawn by
 * its magnitude, so both series rise from one baseline; the only thing done to the
 * number is dropping its sign.
 *
 * 012: two lines over fills that fade to the ground, the Obsidiana chart. The same
 * figures are in a visually hidden table, so a screen reader gets the values the
 * hover tooltip gives everyone else.
 */
export default function MonthlyChart({ series }: { series: MonthTotals[] }) {
  const data = series.map((entry) => ({
    month: entry.month,
    income: entry.income,
    expense: Math.abs(entry.expense),
  }));

  return (
    <Card aria-labelledby="monthly-heading" data-testid="monthly-chart" className="flex flex-col gap-3">
      <SectionHeading id="monthly-heading">Últimos 12 meses</SectionHeading>

      <div className="h-64 text-xs" aria-hidden="true">
        <ResponsiveContainer width="100%" height="100%">
          <AreaChart data={data} margin={{ top: 8, right: 4, bottom: 0, left: 0 }}>
            <defs>
              <Fade id="fade-income" colour="var(--chart-income)" />
              <Fade id="fade-expense" colour="var(--chart-expense)" />
            </defs>
            <CartesianGrid vertical={false} stroke="var(--border)" />
            <XAxis dataKey="month" tickFormatter={shortMonth} tickLine={false} axisLine={false} tick={axisTick} />
            <YAxis tickFormatter={wholeMoney} tickLine={false} axisLine={false} width={64} tick={axisTick} />
            <Tooltip
              {...tooltipProps}
              cursor={{ stroke: 'var(--border)' }}
              labelFormatter={(month) => formatMonth(String(month))}
              formatter={(value) => money(Number(value))}
              // Series order: income first, as drawn.
              itemSorter={(item) => (item.dataKey === 'income' ? 0 : 1)}
            />
            {/* Series order, not alphabetical; the text in ink, the swatch carries the colour. */}
            <Legend
              iconType="circle"
              itemSorter={null}
              formatter={(name) => <span className="text-foreground">{name}</span>}
            />
            <Area
              type="monotone"
              dataKey="income"
              name="Receitas"
              stroke="var(--chart-income)"
              strokeWidth={2}
              fill="url(#fade-income)"
            />
            <Area
              type="monotone"
              dataKey="expense"
              name="Despesas"
              stroke="var(--chart-expense)"
              strokeWidth={2}
              fill="url(#fade-expense)"
            />
          </AreaChart>
        </ResponsiveContainer>
      </div>

      {/* A table does not shrink to sr-only's 1px, so the wrapper carries it. */}
      <div className="sr-only">
        <table>
          <caption>Receitas e despesas por mês</caption>
          <thead>
            <tr>
              <th scope="col">Mês</th>
              <th scope="col">Receitas</th>
              <th scope="col">Despesas</th>
            </tr>
          </thead>
          <tbody>
            {data.map((entry) => (
              <tr key={entry.month}>
                <th scope="row">{formatMonth(entry.month)}</th>
                <td>{money(entry.income)}</td>
                <td>{money(entry.expense)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </Card>
  );
}

function Fade({ id, colour }: { id: string; colour: string }) {
  return (
    <linearGradient id={id} x1="0" y1="0" x2="0" y2="1">
      <stop offset="0%" stopColor={colour} stopOpacity={0.35} />
      <stop offset="100%" stopColor={colour} stopOpacity={0} />
    </linearGradient>
  );
}
