import { Link } from '@tanstack/react-router';
import { useState } from 'react';

import type { ReturnsQuery } from '@/api/finance';
import { usePositions } from '@/components/investments/queries';
import AssetReturnsTable from '@/components/returns/AssetReturnsTable';
import { usePortfolioReturns } from '@/components/returns/queries';
import ReturnsReport from '@/components/returns/ReturnsReport';

/**
 * `/investments/returns` (008): how the money did, and against what. Every figure is the
 * API's; the page formats, it does not compute.
 */
export default function ReturnsPage() {
  const [query, setQuery] = useState<ReturnsQuery>({ period: 'inception' });
  const returns = usePortfolioReturns(query);
  const positions = usePositions();

  return (
    <section className="grid gap-6">
      <div>
        <Link to="/investments" className="text-muted-foreground text-sm underline-offset-4 hover:underline">
          ← Investimentos
        </Link>
        <h2 className="mt-2 text-3xl font-semibold tracking-tight">Rentabilidade</h2>
      </div>

      <ReturnsReport query={query} onQuery={setQuery} returns={returns} />

      {returns.data?.twr && positions.data && <AssetReturnsTable positions={positions.data} query={query} />}
    </section>
  );
}
