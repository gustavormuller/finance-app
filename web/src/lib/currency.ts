import { useSyncExternalStore } from 'react';

import type { Returns, ReturnsPoint } from '@/api/finance';

import { divideToEven } from './decimal';

/**
 * Reais or dollars: which currency the investments area shows its money in.
 *
 * A per-device convenience in `localStorage`, as `lib/theme.ts` keeps the theme, never a
 * server setting. Key `currency`, values `BRL` | `USD`. Storage that cannot be read is R$;
 * a choice that cannot be stored still applies until the page is left.
 */
export type Currency = 'BRL' | 'USD';

const KEY = 'currency';

/** The choice made while storage refused it, which outranks what storage holds. */
let unsaved: Currency | null = null;

const listeners = new Set<() => void>();

export function readCurrencyChoice(): Currency {
  if (unsaved) {
    return unsaved;
  }

  try {
    return localStorage.getItem(KEY) === 'USD' ? 'USD' : 'BRL';
  } catch {
    return 'BRL';
  }
}

export function chooseCurrency(choice: Currency): void {
  try {
    localStorage.setItem(KEY, choice);
    unsaved = null;
  } catch {
    unsaved = choice;
  }

  listeners.forEach((listener) => listener());
}

function subscribe(listener: () => void): () => void {
  listeners.add(listener);
  // Another tab's choice arrives as a `storage` event.
  window.addEventListener('storage', listener);

  return () => {
    listeners.delete(listener);
    window.removeEventListener('storage', listener);
  };
}

/** The current choice, and how to change it; every reader re-renders on a change. */
export function useCurrencyChoice(): [Currency, (choice: Currency) => void] {
  return [useSyncExternalStore(subscribe, readCurrencyChoice), chooseCurrency];
}

const CENTS = 2;
const RATE_PLACES = 8;
const INDEX_PLACES = 6;
const RETURN_PLACES = 10;

/** A figure the API wrote with `places` decimals, as a `bigint` of those units, exactly. */
const units = (value: number, places: number) => BigInt(value.toFixed(places).replace('.', ''));

/**
 * A BRL amount in dollars at `rate` (BRL per dollar), to the cent.
 *
 * The amount has two places (`numeric(18,2)`) and the rate eight (`numeric(18,8)`), so
 * both are read back exactly as the API wrote them, and the quotient is rounded once,
 * half to even, as the API's `Money` rounds. Float division is not: 0.35 / 2 is below
 * 0.175 in float64 and would lose the cent.
 */
export function brlToUsd(brl: number, rate: number): number {
  const rateUnits = units(rate, RATE_PLACES);
  if (rateUnits <= 0n) {
    throw new RangeError(`A rate must be positive, not ${rate}.`);
  }

  // brl / rate in cents = (cents / 100) / (rateUnits / 10^8) x 100 = cents x 10^8 / rateUnits.
  return Number(divideToEven(units(brl, CENTS) * 10n ** BigInt(RATE_PLACES), rateUnits)) / 100;
}

/** The benchmark code whose index is the dollar, base 100. */
const DOLLAR = 'USDBRL';

/**
 * `index / dollar - 1` for two base-100 indices: a period's return in dollars, to ten
 * places as the API rounds rates, exact over the indices' six places.
 */
function returnOver(index: number, dollar: number): number {
  const ratio = divideToEven(units(index, INDEX_PLACES) * 10n ** BigInt(RETURN_PLACES), units(dollar, INDEX_PLACES));

  return Number(ratio - 10n ** BigInt(RETURN_PLACES)) / 10 ** RETURN_PLACES;
}

/** `(1 + total)^(365 / days) - 1`, the API's annualisation; display only, so a float. */
function annualise(total: number, days: number): number | null {
  const rate = days > 0 ? Math.pow(1 + total, 365 / days) - 1 : NaN;

  return Number.isFinite(rate) ? Number(rate.toFixed(RETURN_PLACES)) : null;
}

/**
 * The returns seen from a dollar (016, decision 13). The series carries the dollar as a
 * base-100 index, so a value in dollars is `index / dollar x 100` at every point, for the
 * portfolio and each benchmark alike, and a period's return is the last point over 100.
 *
 * The dollar itself is dropped: in dollars it is the unit, flat at 100. XIRR and the
 * timing effect stay as sent, in reais: an XIRR in dollars needs every flow at its own
 * day's rate, which the series does not carry (decision 14). `null` when there is no
 * dollar index to divide by, or nothing was held.
 */
export function returnsInUsd<T extends Returns>(returns: T): T | null {
  const { period, twr, series } = returns;
  const last = series.at(-1);
  if (!period || !twr || !last || series.some((point) => typeof point[DOLLAR] !== 'number')) {
    return null;
  }

  const codes = Object.keys(returns.benchmarks).filter((code) => code !== DOLLAR);
  const dollarAt = (point: ReturnsPoint) => point[DOLLAR] as number;
  const inDollars = (index: number, point: ReturnsPoint) => Number(((index / dollarAt(point)) * 100).toFixed(INDEX_PLACES));
  const periodReturn = (index: number) => {
    const total = returnOver(index, dollarAt(last));
    return { total, annualised: annualise(total, period.days) };
  };

  return {
    ...returns,
    twr: periodReturn(last.portfolio),
    benchmarks: Object.fromEntries(
      codes.map((code) => {
        const index = last[code];
        return [code, returns.benchmarks[code] && typeof index === 'number' ? periodReturn(index) : null];
      }),
    ),
    series: series.map((point) => {
      const converted: ReturnsPoint = { date: point.date, portfolio: inDollars(point.portfolio, point) };
      for (const code of codes) {
        const index = point[code];
        if (typeof index === 'number') {
          converted[code] = inDollars(index, point);
        }
      }
      return converted;
    }),
  };
}
