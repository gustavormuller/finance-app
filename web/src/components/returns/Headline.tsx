import type { PeriodReturn, ReturnsPeriod } from '@/api/finance';
import { formatRate, perYear, showsAnnualised, signTone } from '@/lib/rates';

/**
 * Spec 008 "Headline": TWR, XIRR and their difference, the timing effect. XIRR is always
 * a year's rate, so the timing effect is too. In dollars (016) only the TWR is converted;
 * XIRR and the timing effect say they are in reais (decision 14).
 */
export default function Headline({
  period,
  twr,
  xirr,
  timingEffect,
  inDollars = false,
}: {
  period: ReturnsPeriod;
  twr: PeriodReturn;
  xirr: number | null;
  timingEffect: number | null;
  inDollars?: boolean;
}) {
  const inReais = inDollars ? ', em reais' : '';

  return (
    <dl className="grid gap-3 sm:grid-cols-3">
      <Stat
        testId="headline-twr"
        label={inDollars ? 'Rentabilidade em dólar (TWR)' : 'Rentabilidade (TWR)'}
        note="O desempenho dos ativos, sem o efeito de quando você aportou."
      >
        <span data-testid="headline-value">
          {formatRate(twr.total)} <span className="text-muted-foreground text-sm font-normal">no período</span>
        </span>
        {showsAnnualised(period) && <span className="block text-base">{perYear(twr.annualised)}</span>}
      </Stat>

      <Stat
        testId="headline-xirr"
        label={`Retorno do dinheiro (XIRR)${inReais}`}
        note="O retorno real, com a data e o valor de cada aporte e resgate."
      >
        <span data-testid="headline-value">{perYear(xirr)}</span>
      </Stat>

      <Stat
        testId="headline-timing"
        label={`Efeito do timing${inReais}`}
        note="Quanto suas decisões de quando aportar ajudaram ou atrapalharam."
      >
        <span data-testid="headline-value" className={signTone(timingEffect)}>
          {perYear(timingEffect)}
        </span>
      </Stat>
    </dl>
  );
}

function Stat({ testId, label, note, children }: { testId: string; label: string; note: string; children: React.ReactNode }) {
  return (
    <div data-testid={testId} className="grid content-start gap-1 glass rounded-2xl p-5">
      <dt className="text-muted-foreground text-sm">{label}</dt>
      <dd className="text-2xl font-semibold tabular-nums">{children}</dd>
      <dd className="text-muted-foreground text-xs">{note}</dd>
    </div>
  );
}
