import { byDateDescending, formatBare, formatDay } from './fixture';

/**
 * D — Statement.
 *
 * Structure carried entirely by rules, alignment and type weight, the way a printed
 * bank statement or an annual report does it. No colour at all: direction is shown
 * the way accounting has always shown it — a debit column and a credit column, so you
 * read which side a number is on rather than what colour it is.
 *
 * That is the real argument for this one. Colour-coded signs fail for the ~8% of men
 * with a red/green deficiency and in a printout; two columns fail for nobody.
 *
 * Serif, because this is the one variant where the reference is print.
 */
export default function VariantD() {
  const serif = '"Iowan Old Style", "Palatino Linotype", Palatino, Georgia, serif';

  return (
    <div className="min-h-screen bg-[#fdfdfb] px-6 py-14 text-black" style={{ fontFamily: serif }}>
      <div className="mx-auto max-w-[860px]">
        <header className="border-b-2 border-black pb-3">
          <h1 className="text-[28px] leading-none font-normal tracking-tight">Statement of account</h1>
          <p className="mt-2 text-[13px] tracking-[0.08em] uppercase">
            25 August — 14 September 2026
          </p>
        </header>

        <table className="w-full border-collapse">
          <thead>
            <tr className="text-[11px] uppercase tracking-[0.12em]">
              <th className="w-[70px] border-b border-black py-2 text-left font-normal">Date</th>
              <th className="border-b border-black py-2 text-left font-normal">Particulars</th>
              <th className="w-[110px] border-b border-black py-2 text-right font-normal">Debit</th>
              <th className="w-[110px] border-b border-black py-2 text-right font-normal">Credit</th>
            </tr>
          </thead>
          <tbody>
            {byDateDescending.map((row) => (
              <tr key={row.id} className="border-b border-[#ddd8cd] align-baseline">
                <td className="py-2.5 text-[14px] tabular-nums">{formatDay(row.date)}</td>
                <td className="py-2.5 pr-6 text-[15px]">
                  {row.description}
                  <span className="ml-2 text-[12px] tracking-[0.06em] uppercase text-[#6b6459]">
                    {row.category} / {row.account}
                  </span>
                </td>
                <td className="py-2.5 text-right text-[15px] tabular-nums">
                  {row.amount < 0 ? formatBare(row.amount) : ''}
                </td>
                <td className="py-2.5 text-right text-[15px] font-semibold tabular-nums">
                  {row.amount > 0 ? formatBare(row.amount) : ''}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
