import { formatDate } from '@/lib/labels';
import { formatUnitPrice } from '@/lib/money';

import type { Display } from './useDisplayCurrency';

/**
 * The line under an investments page's title that says what the money is in:
 * the rate and its day in dollars, why it is still reais when there is no rate, and
 * nothing in reais by choice.
 */
export default function CurrencyNote({ display }: { display: Display }): React.JSX.Element | null {
  if (display.fx) {
    return (
      <span data-testid="currency-note">
        em dólar · US$ 1 = {formatUnitPrice(display.fx.rate, 'BRL')} em {formatDate(display.fx.date).slice(0, 5)}
      </span>
    );
  }

  return display.missingRate ? <span data-testid="currency-note">Sem cotação do dólar sincronizada; valores em reais.</span> : null;
}
