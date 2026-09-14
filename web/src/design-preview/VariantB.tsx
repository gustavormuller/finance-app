import { byDateDescending, formatAmount, formatLongDay } from './fixture';

/**
 * B — Calm.
 *
 * The opposite bet to A: you are not auditing a ledger, you are checking in on your
 * own money, and the number is what you came for. So the amount is the largest thing
 * on every row and everything else recedes to support it.
 *
 * Warm ground (a paper-ish #faf7f2) rather than white or grey — grey reads as
 * software, warm reads as a statement someone posted you. No cards: the whitespace
 * does the separating, and boxing calm content just adds edges back.
 */
export default function VariantB() {
  return (
    <div className="min-h-screen bg-[#faf7f2] px-6 py-12 text-[#2b2622]">
      <div className="mx-auto max-w-[720px]">
        <header className="mb-10">
          <h1 className="text-2xl font-medium tracking-tight">Transactions</h1>
          <p className="mt-1 text-sm text-[#8a7f74]">Three weeks · three accounts</p>
        </header>

        <ul>
          {byDateDescending.map((row) => (
            <li
              key={row.id}
              className="flex items-baseline justify-between gap-6 border-b border-[#e8e0d5] py-5 last:border-b-0"
            >
              <div className="min-w-0">
                <p className="truncate text-[15px]">{row.description}</p>
                <p className="mt-1 text-[13px] text-[#8a7f74]">
                  {formatLongDay(row.date)} · {row.category} · {row.account}
                </p>
              </div>

              <p
                className={`shrink-0 text-right text-[22px] tabular-nums ${
                  row.amount < 0 ? 'text-[#2b2622]' : 'text-[#3f7d53]'
                }`}
              >
                {formatAmount(row.amount)}
              </p>
            </li>
          ))}
        </ul>
      </div>
    </div>
  );
}
