import { byDateDescending, formatAmount, formatDay } from './fixture';

/**
 * A — Ledger.
 *
 * Density as the organising idea: this is a book of record, and the value is in
 * seeing a whole month at once. Hairlines instead of cards, 28px rows, and colour
 * used for exactly one job — telling the sign apart. Everything else is greyscale, so
 * the two greens on the page are the only things that catch the eye.
 *
 * Sized so all 25 rows are visible without scrolling at 1440x900.
 */
export default function VariantA() {
  return (
    <div className="min-h-screen bg-white px-6 py-5 text-[13px] text-neutral-900">
      <div className="mx-auto max-w-[1100px]">
        <header className="mb-3 flex items-baseline justify-between border-b border-neutral-300 pb-2">
          <h1 className="text-[15px] font-semibold tracking-tight">Transactions</h1>
          <span className="text-[11px] uppercase tracking-[0.14em] text-neutral-500">
            25 movements · 25 Aug – 14 Sep 2026
          </span>
        </header>

        <div className="overflow-x-auto">
          <table className="w-full min-w-[760px] border-collapse">
            <thead>
              <tr className="text-[10px] uppercase tracking-[0.14em] text-neutral-500">
                <th className="w-[62px] border-b border-neutral-300 py-1.5 text-left font-medium">Date</th>
                <th className="border-b border-neutral-300 py-1.5 text-left font-medium">Description</th>
                <th className="w-[130px] border-b border-neutral-300 py-1.5 text-left font-medium">Category</th>
                <th className="w-[90px] border-b border-neutral-300 py-1.5 text-left font-medium">Account</th>
                <th className="w-[120px] border-b border-neutral-300 py-1.5 text-right font-medium">Amount</th>
              </tr>
            </thead>
            <tbody>
              {byDateDescending.map((row) => (
                <tr key={row.id} className="border-b border-neutral-150 [border-bottom-color:#ececec]">
                  <td className="py-[5px] text-neutral-500 tabular-nums">{formatDay(row.date)}</td>
                  <td className="py-[5px] pr-4 whitespace-nowrap overflow-hidden text-ellipsis">
                    {row.description}
                  </td>
                  <td className="py-[5px] text-neutral-600">{row.category}</td>
                  <td className="py-[5px] text-neutral-500">{row.account}</td>
                  <td
                    className={`py-[5px] text-right tabular-nums ${
                      row.amount < 0 ? 'text-neutral-900' : 'text-emerald-700'
                    }`}
                  >
                    {formatAmount(row.amount)}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}
