import { Link, useParams } from '@tanstack/react-router';
import { useState } from 'react';

import type { ReturnsQuery } from '@/api/finance';
import CurrencyToggle from '@/components/investments/CurrencyToggle';
import { usePositions } from '@/components/investments/queries';
import FxSplit from '@/components/returns/FxSplit';
import { useAssetReturns } from '@/components/returns/queries';
import ReturnsReport from '@/components/returns/ReturnsReport';
import { useCurrencyChoice } from '@/lib/currency';

/** `/investments/{id}/returns`: one asset's returns, and its FX split when not in reais. */
export default function AssetReturnsPage() {
  const { assetId } = useParams({ from: '/protected/investments/$assetId/returns' });
  const [query, setQuery] = useState<ReturnsQuery>({ period: 'inception' });
  const [currency] = useCurrencyChoice();
  const returns = useAssetReturns(assetId, query);
  const position = usePositions().data?.find((candidate) => candidate.assetId === assetId);
  const ticker = position?.ticker ?? 'Ativo';

  return (
    <section className="grid gap-6 [&>*]:min-w-0">
      <div>
        <Link to="/investments/$assetId" params={{ assetId }} className="text-muted-foreground text-sm underline-offset-4 hover:underline">
          ← {ticker}
        </Link>
        <div className="mt-2 flex flex-wrap items-end justify-between gap-4">
          <h2 className="text-3xl font-semibold tracking-tight">{ticker} · Rentabilidade</h2>
          <CurrencyToggle />
        </div>
      </div>

      <ReturnsReport
        query={query}
        onQuery={setQuery}
        returns={returns}
        currency={currency}
        extra={returns.data?.fx && <FxSplit fx={returns.data.fx} currency={position?.currency ?? 'moeda original'} />}
      />
    </section>
  );
}
