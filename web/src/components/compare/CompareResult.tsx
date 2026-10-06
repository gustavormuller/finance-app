import type { UseQueryResult } from '@tanstack/react-query';

import { ApiError, type Comparison } from '@/api/finance';
import Alert from '@/components/Alert';
import { formatDate } from '@/lib/labels';
import { cn } from '@/lib/utils';

import CompareChart from './CompareChart';
import CompareTable from './CompareTable';
import type { SeriesLabel } from './series';

const list = new Intl.ListFormat('pt-BR', { style: 'long', type: 'conjunction' });

function Notice({ children }: { children: React.ReactNode }) {
  return <div className="text-muted-foreground glass grid gap-2 rounded-2xl px-6 py-12 text-center text-sm">{children}</div>;
}

/**
 * What the comparison answered (decision 9): the period, why the start is where it is, which
 * series are left out, the chart and the table; or why nothing is drawn. While a new period
 * or currency loads, the last answer stays on screen, dimmed.
 */
export default function CompareResult({
  result,
  labels,
  log,
}: {
  result: UseQueryResult<Comparison>;
  labels: SeriesLabel[];
  log: boolean;
}) {
  if (result.isError) {
    const refused = result.error instanceof ApiError && result.error.status === 400 ? Object.values(result.error.fields).flat() : [];

    return (
      <Alert>{refused.length > 0 ? refused.join(' ') : 'Não foi possível carregar a comparação. Recarregue a página para tentar de novo.'}</Alert>
    );
  }

  if (!result.data) {
    return <p className="text-muted-foreground text-sm">Carregando…</p>;
  }

  const comparison = result.data;
  const name = (position: number) => labels[position]?.label ?? '';
  const { period } = comparison;

  if (period === null) {
    return comparison.series.every((series) => !series.hasData) ? (
      <Notice>
        <p className="text-foreground font-medium">Nenhuma das séries tem dados no período escolhido.</p>
        <p>Escolha um período mais longo, ou Máx.</p>
      </Notice>
    ) : (
      <Notice>
        <p className="text-foreground font-medium">As séries escolhidas não têm um período em comum.</p>
        <ul className="grid gap-1">
          {comparison.series.map((series, position) => (
            <li key={series.key}>
              {name(position)}:{' '}
              {series.firstDate && series.lastDate
                ? `dados de ${formatDate(series.firstDate)} a ${formatDate(series.lastDate)}`
                : 'sem dados'}
            </li>
          ))}
        </ul>
      </Notice>
    );
  }

  const startsLater = period.startMoved
    ? comparison.series.flatMap((series, position) => (series.change !== null && series.firstDate === period.from ? [name(position)] : []))
    : [];
  const leftOut = comparison.series.flatMap((series, position) => (series.hasData ? [] : [name(position)]));

  return (
    <div className={cn('grid gap-4 transition-opacity [&>*]:min-w-0', result.isPlaceholderData && 'opacity-60')}>
      <div className="text-muted-foreground grid gap-1 text-sm">
        <p data-testid="compare-period">
          {formatDate(period.from)} a {formatDate(period.to)} · {period.days.toLocaleString('pt-BR')} {period.days === 1 ? 'dia' : 'dias'}
        </p>
        {startsLater.length > 0 && (
          <p>
            Começa em {formatDate(period.from)}, primeiro dia com dados de {list.format(startsLater)}.
          </p>
        )}
        {leftOut.length > 0 && <p>Sem dados no período escolhido, fora do gráfico: {list.format(leftOut)}.</p>}
      </div>

      <CompareChart comparison={comparison} labels={labels} log={log} />
      <CompareTable comparison={comparison} labels={labels} />
    </div>
  );
}
