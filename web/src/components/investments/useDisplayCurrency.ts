import type { UsdBrl } from '@/api/finance';
import { brlToUsd, useCurrencyChoice, type Currency } from '@/lib/currency';
import { formatCompactMoney, formatMoney, formatSignedMoney } from '@/lib/money';

import { usePortfolioSummary } from './queries';

/**
 * How the investments area writes money right now: in reais, or in dollars at the
 * summary's latest USDBRL. `chosen` is what the toggle says; `currency` is what money is
 * actually shown in, which stays R$ while there is no rate (`missingRate`).
 */
export interface Display {
  chosen: Currency;
  currency: Currency;
  fx: UsdBrl | null;
  missingRate: boolean;
  /** A BRL amount in the display currency, to the cent. */
  convert: (brl: number) => number;
  money: (brl: number) => string;
  signedMoney: (brl: number) => string;
  compactMoney: (brl: number) => string;
}

function displayIn(chosen: Currency, fx: UsdBrl | null, missingRate = false): Display {
  const currency: Currency = fx ? 'USD' : 'BRL';
  const convert = (brl: number) => (fx ? brlToUsd(brl, fx.rate) : brl);

  return {
    chosen,
    currency,
    fx,
    missingRate,
    convert,
    money: (brl) => formatMoney(convert(brl), currency),
    signedMoney: (brl) => formatSignedMoney(convert(brl), currency),
    compactMoney: (brl) => formatCompactMoney(convert(brl), currency),
  };
}

export function useDisplayCurrency(): Display {
  const [chosen] = useCurrencyChoice();
  const summary = usePortfolioSummary();
  const fx = chosen === 'USD' ? (summary.data?.usdBrl ?? null) : null;

  return displayIn(chosen, fx, chosen === 'USD' && summary.data !== undefined && fx === null);
}
