import type { ReturnsPeriod } from '@/api/finance';

/**
 * How 008's rates are written on screen. Display only: every rate arrives computed by
 * the API, and the one figure made here, a difference, is made exactly in `decimal.ts`.
 */

/** What a rate the API could not compute (`null`) reads as. Never NaN, never a blank. */
export const NO_DATA = 'Sem dados';

const percent = new Intl.NumberFormat('pt-BR', {
  style: 'percent',
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
  signDisplay: 'exceptZero',
});

/** `0.0927` as `+9,27%`; `null` as {@link NO_DATA}. */
export function formatRate(fraction: number | null | undefined): string {
  return fraction === null || fraction === undefined ? NO_DATA : percent.format(fraction);
}

const points = new Intl.NumberFormat('pt-BR', {
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
  signDisplay: 'exceptZero',
});

/**
 * Hundredths of a percentage point (from `pointsDifference`) as `-9,12 p.p.`. The
 * value is already rounded, so dividing a whole number by 100 prints it exactly.
 */
export function formatPoints(hundredths: bigint): string {
  return `${points.format(Number(hundredths) / 100)} p.p.`;
}

/**
 * The spec's annualisation rule: "annualise when the period exceeds one year". A year
 * or less shows the period's return only (DEFERRED, 008 · CP4 and CP5).
 */
export const showsAnnualised = (period: ReturnsPeriod) => period.days > 365;

/** A year's rate: `+8,31% a.a.`, or "Sem dados" with no suffix. */
export const perYear = (rate: number | null) => (rate === null ? formatRate(null) : `${formatRate(rate)} a.a.`);

/** Spec 008 UI: a gain green, a loss the expense colour, zero and no data in ink. */
export function signTone(rate: number | null): string | undefined {
  if (rate === null || rate === 0) {
    return undefined;
  }

  return rate > 0 ? 'text-green-700 dark:text-green-500' : 'text-chart-expense';
}

const indexTick = new Intl.NumberFormat('pt-BR', { maximumFractionDigits: 2 });

/**
 * A base-100 axis tick: `101,5`, `100`. The fraction is kept, because over a short period
 * the axis spans a few points, and ticks rounded to whole numbers repeat.
 */
export const formatIndexTick = (value: number) => indexTick.format(value);
