import { Link, useParams } from '@tanstack/react-router';
import { useState } from 'react';

import type { ReturnsQuery } from '@/api/finance';
import { usePositions } from '@/components/investments/queries';
import FxSplit from '@/components/returns/FxSplit';
import { useAssetReturns } from '@/components/returns/queries';
import ReturnsReport from '@/components/returns/ReturnsReport';

/** `/investments/{id}/returns` (008): one asset's returns, and its FX split when not in reais. */
export default function AssetReturnsPage() {
  const { assetId } = useParams({ from: '/protected/investments/$assetId/returns' });
  const [query, setQuery] = useState<ReturnsQuery>({ period: 'inception' });
  const returns = useAssetReturns(assetId, query);
  const position = usePositions().data?.find((candidate) => candidate.assetId === assetId);
  const ticker = position?.ticker ?? 'Ativo';

  return (
    <section className="grid gap-6 [&>*]:min-w-0">
      <div>
        <Link to="/investments/$assetId" params={{ assetId }} className="text-muted-foreground text-sm underline-offset-4 hover:underline">
          ← {ticker}
        </Link>
        <h2 className="mt-2 text-3xl font-semibold tracking-tight">{ticker} · Rentabilidade</h2>
      </div>

      <ReturnsReport
        query={query}
        onQuery={setQuery}
        returns={returns}
        extra={returns.data?.fx && <FxSplit fx={returns.data.fx} currency={position?.currency ?? 'moeda original'} />}
      />
    </section>
  );
}
