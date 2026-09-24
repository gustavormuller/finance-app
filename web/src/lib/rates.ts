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
