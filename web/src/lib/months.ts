/**
 * Calendar months as the API writes them, `YYYY-MM`.
 *
 * The dashboard always sends the month it means, computed here from the local date:
 * the API's own default is the UTC month, which runs ahead of Brasília for three
 * hours at every month's turn (005 checkpoint 2).
 */

const MONTH_NAMES = [
  'janeiro',
  'fevereiro',
  'março',
  'abril',
  'maio',
  'junho',
  'julho',
  'agosto',
  'setembro',
  'outubro',
  'novembro',
  'dezembro',
];

function parts(month: string): [number, number] {
  const [year, number] = month.split('-').map(Number);

  return [year ?? 0, number ?? 1];
}

function write(year: number, monthIndex: number): string {
  return `${year}-${String(monthIndex + 1).padStart(2, '0')}`;
}

/** The local calendar month, never the UTC one. */
export function currentMonth(): string {
  const now = new Date();

  return write(now.getFullYear(), now.getMonth());
}

/** `delta` months later (or earlier, when negative). Integer arithmetic, no `Date`. */
export function shiftMonth(month: string, delta: number): string {
  const [year, number] = parts(month);
  const index = year * 12 + (number - 1) + delta;

  return write(Math.floor(index / 12), ((index % 12) + 12) % 12);
}

/** `2026-09` as `setembro de 2026`. */
export function formatMonth(month: string): string {
  const [year, number] = parts(month);

  return `${MONTH_NAMES[number - 1]} de ${year}`;
}

/** `2026-01` as `jan/26`, for a chart axis. */
export function shortMonth(month: string): string {
  const [year, number] = parts(month);

  return `${MONTH_NAMES[number - 1]?.slice(0, 3)}/${String(year).slice(-2)}`;
}

/**
 * The last `count` entries of an oldest-first series, up to `through` inclusive.
 * The API's monthly series ends at its UTC month, so the caller asks for one extra
 * and this drops a month that has not started locally yet.
 */
export function lastMonths<T extends { month: string }>(series: T[], through: string, count: number): T[] {
  // `YYYY-MM` strings compare in calendar order.
  return series.filter((entry) => entry.month <= through).slice(-count);
}
