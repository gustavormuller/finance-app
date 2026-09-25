import { Link } from '@tanstack/react-router';
import { useState } from 'react';

import type { ReturnsQuery } from '@/api/finance';
import CurrencyToggle from '@/components/investments/CurrencyToggle';
import { usePositions } from '@/components/investments/queries';
import AssetReturnsTable from '@/components/returns/AssetReturnsTable';
import { usePortfolioReturns } from '@/components/returns/queries';
import ReturnsReport from '@/components/returns/ReturnsReport';
import { returnsInUsd, useCurrencyChoice } from '@/lib/currency';

/**
 * `/investments/returns` (008): how the money did, and against what. Every figure is the
 * API's; the page formats, it does not compute. 016 keeps it as the detailed report
 * behind `/investments`' hero, in R$ or US$.
 */
export default function ReturnsPage() {
  const [query, setQuery] = useState<ReturnsQuery>({ period: 'inception' });
  const [currency] = useCurrencyChoice();
  const returns = usePortfolioReturns(query);
  const positions = usePositions();
  const inDollars = currency === 'USD' && !!returns.data && returnsInUsd(returns.data) !== null;

  return (
    <section className="grid gap-6 [&>*]:min-w-0">
      <div>
        <Link to="/investments" className="text-muted-foreground text-sm underline-offset-4 hover:underline">
          ← Investimentos
        </Link>
        <div className="mt-2 flex flex-wrap items-end justify-between gap-4">
          <h2 className="text-3xl font-semibold tracking-tight">Rentabilidade</h2>
          <CurrencyToggle />
        </div>
      </div>

      <ReturnsReport query={query} onQuery={setQuery} returns={returns} currency={currency} />

      {returns.data?.twr && positions.data && <AssetReturnsTable positions={positions.data} query={query} inDollars={inDollars} />}
    </section>
  );
}
