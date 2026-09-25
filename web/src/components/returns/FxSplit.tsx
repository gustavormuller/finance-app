import type { FxSplit as Split } from '@/api/finance';
import { formatRate } from '@/lib/rates';

/**
 * The FX decomposition of a non-BRL asset over the period: the asset in its own
 * currency, the exchange rate, and the total in reais, `(1 + total) = (1 + native)(1 + fx)`.
 */
export default function FxSplit({ fx, currency }: { fx: Split; currency: string }) {
  const parts: [string, string, number][] = [
    ['fx-native', `Ativo em ${currency}`, fx.native],
    ['fx-fx', 'Câmbio', fx.fx],
    ['fx-total', 'Total em reais', fx.total],
  ];

  return (
    <dl data-testid="fx-split" className="grid grid-cols-3 gap-3 glass rounded-2xl p-5">
      {parts.map(([testId, label, rate]) => (
        <div key={testId} data-testid={testId}>
          <dt className="text-muted-foreground text-sm">{label}</dt>
          <dd className="text-lg font-semibold tabular-nums">{formatRate(rate)}</dd>
        </div>
      ))}
    </dl>
  );
}
