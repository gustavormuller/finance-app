import { Bar, BarChart, CartesianGrid, Legend, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';

import type { MonthTotals } from '@/api/finance';
import { formatMonth, shortMonth } from '@/lib/months';

import SectionHeading from './SectionHeading';

const money = (value: number) =>
  value.toLocaleString('pt-BR', { minimumFractionDigits: 2, maximumFractionDigits: 2 });

/** Axis ticks only: whole reais with pt-BR grouping, so the axis stays narrow. */
const tick = (value: number) => value.toLocaleString('pt-BR', { maximumFractionDigits: 0 });

/**
 * Section 3: income and expense per month, grouped bars. Expense arrives negative
 * and is drawn by its magnitude, so both bars grow up from one baseline; the only
 * thing done to the number is dropping its sign.
 *
 * The same figures are in a visually hidden table, so a screen reader gets the
 * values the hover tooltip gives everyone else.
 */
export default function MonthlyChart({ series }: { series: MonthTotals[] }) {
  const data = series.map((entry) => ({
    month: entry.month,
    income: entry.income,
    expense: Math.abs(entry.expense),
  }));

  return (
    <section aria-labelledby="monthly-heading" data-testid="monthly-chart">
      <SectionHeading id="monthly-heading">Últimos 12 meses</SectionHeading>

      <div className="mt-3 h-64 text-xs" aria-hidden="true">
        <ResponsiveContainer width="100%" height="100%">
          <BarChart data={data} barGap={2} margin={{ top: 8, right: 0, bottom: 0, left: 0 }}>
            <CartesianGrid vertical={false} stroke="var(--border)" />
            <XAxis dataKey="month" tickFormatter={shortMonth} tickLine={false} axisLine={false} />
            <YAxis tickFormatter={tick} tickLine={false} axisLine={false} width={64} />
            <Tooltip
              cursor={{ fill: 'var(--muted)' }}
              labelFormatter={(month) => formatMonth(String(month))}
              formatter={(value) => money(Number(value))}
              contentStyle={{ background: 'var(--popover)', border: '1px solid var(--border)', borderRadius: 8 }}
            />
            <Legend iconType="circle" />
            <Bar dataKey="income" name="Receitas" fill="var(--chart-income)" radius={[4, 4, 0, 0]} />
            <Bar dataKey="expense" name="Despesas" fill="var(--chart-expense)" radius={[4, 4, 0, 0]} />
          </BarChart>
        </ResponsiveContainer>
      </div>

      <table className="sr-only">
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
    </section>
  );
}
