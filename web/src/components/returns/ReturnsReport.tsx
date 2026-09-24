import type { UseQueryResult } from '@tanstack/react-query';

import { ApiError, type Returns, type ReturnsQuery } from '@/api/finance';
import Alert from '@/components/Alert';
import { formatDate } from '@/lib/labels';
import { showsAnnualised } from '@/lib/rates';

import { orderedCodes } from './benchmarks';
import BenchmarksTable from './BenchmarksTable';
import ComparisonChart from './ComparisonChart';
import Headline from './Headline';
import PeriodSelector from './PeriodSelector';
import { fieldErrors } from './queries';

/**
 * What the portfolio's and an asset's returns pages share: the period selector, the
 * headline, the chart and the benchmark table, over one response. `extra` goes under
 * the headline (an asset's FX split). Every figure is the API's.
 */
export default function ReturnsReport({
  query,
  onQuery,
  returns,
  extra,
}: {
  query: ReturnsQuery;
  onQuery: (query: ReturnsQuery) => void;
  returns: UseQueryResult<Returns>;
  extra?: React.ReactNode;
}) {
  const errors = fieldErrors(returns.error);
  const { data } = returns;

  return (
    <>
      <PeriodSelector query={query} errors={errors} onChange={onQuery} />

      {errors.period && <Alert>{errors.period.join(' ')}</Alert>}
      {returns.error instanceof ApiError && returns.error.status === 404 ? (
        <p className="text-muted-foreground text-sm">Ativo não encontrado.</p>
      ) : (
        returns.isError &&
        Object.keys(errors).length === 0 && <Alert>Não foi possível carregar a rentabilidade. Recarregue a página para tentar de novo.</Alert>
      )}
      {returns.isPending && <p className="text-muted-foreground text-sm">Carregando…</p>}

      {data &&
        (data.period === null || data.twr === null ? (
          <p className="border-border text-muted-foreground border-t py-12 text-center text-sm">Nenhuma posição valorizada neste período.</p>
        ) : (
          <>
            <p data-testid="returns-period" className="text-muted-foreground -mt-4 text-sm">
              {formatDate(data.period.from)} a {formatDate(data.period.to)} · {data.period.days} {data.period.days === 1 ? 'dia' : 'dias'}
            </p>
            <Headline period={data.period} twr={data.twr} xirr={data.xirr} timingEffect={data.timingEffect} />
            {extra}
            <ComparisonChart series={data.series} codes={orderedCodes(data.benchmarks).filter((code) => data.benchmarks[code])} />
            <BenchmarksTable
              twr={data.twr}
              benchmarks={data.benchmarks}
              codes={orderedCodes(data.benchmarks)}
              annualised={showsAnnualised(data.period)}
            />
          </>
        ))}
    </>
  );
}
