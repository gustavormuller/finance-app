import { byDateDescending, formatAmount, formatDay, largestAmount } from './fixture';

/**
 * E — Magnitude.
 *
 * A table where the scanning is visual rather than numeric: each row carries a bar
 * proportional to its amount, so "the rent and the salary are the two that matter
 * this month" is something you see before you have read a single figure.
 *
 * Bars are scaled against the largest absolute amount in the range, and income grows
 * right from the centre line while expense grows left. A shared centre is what makes
 * the two directions comparable at a glance; two separate left-anchored scales would
 * not be.
 *
 * The risk this variant is here to expose: with a 12,847 salary in range, a 4.50
 * coffee is a bar less than a pixel wide. The numbers stay on the row precisely
 * because the bars cannot be trusted at the small end.
 */
export default function VariantE() {
  return (
    <div className="min-h-screen bg-white px-6 py-8 text-[14px] text-neutral-900">
      <div className="mx-auto max-w-[1000px]">
        <header className="mb-5">
          <h1 className="text-xl font-semibold tracking-tight">Transactions</h1>
          <p className="mt-1 text-[13px] text-neutral-500">
            Bars scaled to the largest movement in range ({largestAmount.toLocaleString('pt-BR', {
              minimumFractionDigits: 2,
              maximumFractionDigits: 2,
            })})
          </p>
        </header>

        <table className="w-full border-collapse">
          <thead>
            <tr className="text-[11px] uppercase tracking-[0.1em] text-neutral-500">
              <th className="w-[64px] border-b border-neutral-200 py-2 text-left font-medium">Date</th>
              <th className="border-b border-neutral-200 py-2 text-left font-medium">Description</th>
              <th className="w-[260px] border-b border-neutral-200 py-2 text-center font-medium">
                Out · In
              </th>
              <th className="w-[120px] border-b border-neutral-200 py-2 text-right font-medium">Amount</th>
            </tr>
          </thead>
          <tbody>
            {byDateDescending.map((row) => {
              const share = Math.abs(row.amount) / largestAmount;
              const outgoing = row.amount < 0;

              return (
                <tr key={row.id} className="border-b border-neutral-100">
                  <td className="py-2 text-neutral-500 tabular-nums">{formatDay(row.date)}</td>
                  <td className="py-2 pr-4">
                    {row.description}
                    <span className="ml-2 text-[12px] text-neutral-500">{row.category}</span>
                  </td>
                  <td className="py-2">
                    <div className="relative h-3">
                      <div className="absolute inset-y-0 left-1/2 w-px bg-neutral-300" />
                      <div
                        className={`absolute inset-y-0 ${
                          outgoing ? 'right-1/2 bg-rose-400' : 'left-1/2 bg-emerald-500'
                        }`}
                        style={{ width: `${Math.max(share * 50, 0.4)}%` }}
                      />
                    </div>
                  </td>
                  <td
                    className={`py-2 text-right tabular-nums ${
                      outgoing ? 'text-neutral-900' : 'text-emerald-700'
                    }`}
                  >
                    {formatAmount(row.amount)}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </div>
  );
}
