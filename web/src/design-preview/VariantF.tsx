import { byDateDescending, formatAmount, formatLongDay, groupByDay } from './fixture';

/**
 * F — Grouped.
 *
 * Not a restyle: a different question. A flat list answers "what did I spend on?";
 * this answers "what did each day cost me?", by making the day the unit and putting a
 * net subtotal on every one of them.
 *
 * That is the shape you want when reconciling against a bank app, which also thinks
 * in days — and it is the only one of the six where a day with an expense and a
 * refund on it reads as roughly nothing happening, which is the truth.
 *
 * The cost, and it is real: 25 rows become 25 rows plus 17 day headers, so the same
 * range no longer fits on a screen. Density is what this trades away.
 */
export default function VariantF() {
  const days = groupByDay(byDateDescending);

  return (
    <div className="min-h-screen bg-neutral-50 px-6 py-8 text-[14px] text-neutral-900">
      <div className="mx-auto max-w-[760px]">
        <header className="mb-6">
          <h1 className="text-xl font-semibold tracking-tight">Transactions</h1>
          <p className="mt-1 text-[13px] text-neutral-500">By day, with a net per day</p>
        </header>

        {days.map((day) => (
          <section key={day.date} className="mb-5">
            <div className="flex items-baseline justify-between border-b border-neutral-300 pb-1.5">
              <h2 className="text-[12px] font-semibold uppercase tracking-[0.1em] text-neutral-600">
                {formatLongDay(day.date)}
              </h2>
              <span
                className={`text-[13px] font-semibold tabular-nums ${
                  day.subtotal < 0 ? 'text-neutral-700' : 'text-emerald-700'
                }`}
              >
                {formatAmount(day.subtotal)}
              </span>
            </div>

            <ul>
              {day.items.map((row) => (
                <li
                  key={row.id}
                  className="flex items-baseline justify-between gap-4 border-b border-neutral-200 py-2.5 last:border-b-0"
                >
                  <div className="min-w-0">
                    <p className="truncate">{row.description}</p>
                    <p className="text-[12px] text-neutral-500">
                      {row.category} · {row.account}
                    </p>
                  </div>
                  <span
                    className={`shrink-0 tabular-nums ${
                      row.amount < 0 ? 'text-neutral-900' : 'text-emerald-700'
                    }`}
                  >
                    {formatAmount(row.amount)}
                  </span>
                </li>
              ))}
            </ul>
          </section>
        ))}
      </div>
    </div>
  );
}
