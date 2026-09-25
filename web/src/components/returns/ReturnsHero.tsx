import { useState } from 'react';

import type { PeriodReturn, ReturnsPeriodKind } from '@/api/finance';
import Alert from '@/components/Alert';
import Card from '@/components/Card';
import { returnsInUsd, type Currency } from '@/lib/currency';
import { pointsDifference } from '@/lib/decimal';
import { benchmarkShortLabel, formatDate, returnsPeriodLabels } from '@/lib/labels';
import { NO_DATA, formatPoints, formatRate, perYear, showsAnnualised, signTone } from '@/lib/rates';
import { cn } from '@/lib/utils';

import ComparisonChart from './ComparisonChart';
import { usePortfolioReturns } from './queries';

type Preset = Exclude<ReturnsPeriodKind, 'custom'>;

/** P1's order: the short periods first, the default last. */
const PRESETS: Preset[] = ['ytd', '12m', 'inception'];

/** What the hero compares with (016, decision 2); SELIC and the dollar are in the report. */
const CODES = ['CDI', 'IPCA6', 'IVVB11'];

/**
 * The portfolio's returns, first thing on `/investments` (016, design P1): the period's
 * TWR large, the annual rate beside it past a year, the XIRR line, three benchmarks and
 * the comparison chart. Every figure is the API's; in dollars the TWR and the benchmarks are
 * derived from the series (decision 13) and the XIRR stays in reais (decision 14).
 */
export default function ReturnsHero({ currency }: { currency: Currency }) {
  const [period, setPeriod] = useState<Preset>('inception');
  const returns = usePortfolioReturns({ period });
  const inDollars = currency === 'USD' && returns.data ? returnsInUsd(returns.data) : null;
  const data = inDollars ?? returns.data;
  const noDollar = currency === 'USD' && returns.data?.twr && !inDollars;

  return (
    <Card aria-labelledby="hero-heading" data-testid="returns-hero" className="grid gap-5">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div className="grid min-w-0 gap-1.5">
          <h3 id="hero-heading" className="text-muted-foreground font-sans text-sm font-semibold">
            Rentabilidade da carteira{inDollars && ' em dólar'}
            {data?.period && ` · desde ${formatDate(data.period.from)}`}
          </h3>

          {data?.period && data.twr && (
            <>
              <p className="flex flex-wrap items-baseline gap-x-4 gap-y-1">
                <span
                  data-testid="hero-twr"
                  className={cn('font-display text-5xl font-semibold tracking-tight tabular-nums sm:text-7xl', signTone(data.twr.total))}
                >
                  {formatRate(data.twr.total)}
                </span>
                <span data-testid="hero-annualised" className="text-muted-foreground text-lg font-semibold tabular-nums sm:text-xl">
                  {showsAnnualised(data.period)
                    ? perYear(data.twr.annualised)
                    : `em ${data.period.days} ${data.period.days === 1 ? 'dia' : 'dias'}`}
                </span>
              </p>
              <p data-testid="hero-xirr" className="text-muted-foreground text-sm">
                Retorno do seu dinheiro (considerando quando você aportou):{' '}
                <b className="text-foreground tabular-nums">{perYear(data.xirr)}</b>
                {inDollars && ' (em reais)'}
              </p>
            </>
          )}
        </div>

        <div role="group" aria-label="Período" className="bg-secondary flex flex-wrap gap-0.5 rounded-xl p-1">
          {PRESETS.map((preset) => (
            <button
              key={preset}
              type="button"
              aria-pressed={period === preset}
              onClick={() => setPeriod(preset)}
              className={cn(
                'text-muted-foreground hover:text-foreground focus-visible:ring-ring/50 h-8 rounded-lg px-3 text-sm font-semibold transition-colors outline-none focus-visible:ring-[3px]',
                period === preset && 'bg-card text-foreground shadow-sm',
              )}
            >
              {returnsPeriodLabels[preset]}
            </button>
          ))}
        </div>
      </div>

      {returns.isPending && <p className="text-muted-foreground text-sm">Carregando…</p>}
      {returns.isError && <Alert>Não foi possível carregar a rentabilidade. Recarregue a página para tentar de novo.</Alert>}
      {data && !data.twr && <p className="text-muted-foreground py-6 text-center text-sm">Nenhuma posição valorizada neste período.</p>}
      {noDollar && <p className="text-muted-foreground text-sm">Rentabilidade em reais: sem cotação do dólar no início do período.</p>}

      {data?.twr && (
        <>
          <dl className="grid gap-3 sm:grid-cols-3">
            {CODES.map((code) => (
              <Comparison key={code} code={code} twr={data.twr} benchmark={data.benchmarks[code] ?? null} />
            ))}
          </dl>
          <ComparisonChart
            bare
            title={inDollars ? 'Carteira e referências em dólar, base 100' : 'Carteira e referências, base 100'}
            series={data.series}
            codes={CODES.filter((code) => data.benchmarks[code])}
          />
        </>
      )}
    </Card>
  );
}

/** One tile: the benchmark's return, and the portfolio's minus it, in points, exactly. */
function Comparison({ code, twr, benchmark }: { code: string; twr: PeriodReturn | null; benchmark: PeriodReturn | null }) {
  const difference = twr && benchmark ? pointsDifference(twr.total, benchmark.total) : null;

  return (
    <div data-testid={`hero-benchmark-${code}`} className="bg-secondary grid min-w-0 content-start gap-0.5 rounded-xl px-4 py-3">
      <dt className="text-muted-foreground text-sm">
        vs {benchmarkShortLabel(code)}
        {benchmark && <b className="text-foreground tabular-nums"> {formatRate(benchmark.total)}</b>}
      </dt>
      <dd className={cn('text-lg font-bold tabular-nums', difference === null ? 'text-muted-foreground' : signTone(Number(difference)))}>
        {difference === null ? NO_DATA : formatPoints(difference)}
      </dd>
    </div>
  );
}
