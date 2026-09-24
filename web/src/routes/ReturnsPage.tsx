import { Link } from '@tanstack/react-router';
import { useState } from 'react';

import type { ReturnsQuery } from '@/api/finance';
import Alert from '@/components/Alert';
import Headline from '@/components/returns/Headline';
import PeriodSelector from '@/components/returns/PeriodSelector';
import { fieldErrors, usePortfolioReturns } from '@/components/returns/queries';
import { formatDate } from '@/lib/labels';

/**
 * `/investments/returns` (008): how the money did, and against what. Every figure is the
 * API's; the page formats, it does not compute.
 */
export default function ReturnsPage() {
  const [query, setQuery] = useState<ReturnsQuery>({ period: 'inception' });
  const returns = usePortfolioReturns(query);
  const errors = fieldErrors(returns.error);

  return (
    <section className="mx-auto grid max-w-5xl gap-8 px-4 py-8">
      <div>
        <Link to="/investments" className="text-muted-foreground text-sm underline-offset-4 hover:underline">
          ← Investimentos
        </Link>
        <h2 className="mt-2 text-2xl font-semibold tracking-tight">Rentabilidade</h2>
      </div>

      <PeriodSelector query={query} errors={errors} onChange={setQuery} />

      {errors.period && <Alert>{errors.period.join(' ')}</Alert>}
      {returns.isError && Object.keys(errors).length === 0 && (
        <Alert>Não foi possível carregar a rentabilidade. Recarregue a página para tentar de novo.</Alert>
      )}
      {returns.isPending && <p className="text-muted-foreground text-sm">Carregando…</p>}

      {returns.data &&
        (returns.data.period === null || returns.data.twr === null ? (
          <p className="border-border text-muted-foreground border-t py-12 text-center text-sm">
            Nenhuma posição valorizada neste período.
          </p>
        ) : (
          <>
            <p data-testid="returns-period" className="text-muted-foreground -mt-4 text-sm">
              {formatDate(returns.data.period.from)} a {formatDate(returns.data.period.to)} · {returns.data.period.days} dias
            </p>
            <Headline
              period={returns.data.period}
              twr={returns.data.twr}
              xirr={returns.data.xirr}
              timingEffect={returns.data.timingEffect}
            />
          </>
        ))}
    </section>
  );
}
