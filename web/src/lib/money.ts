/**
 * How 007's figures are written on screen: pt-BR, with the currency's own symbol.
 *
 * Display only. Every value here arrives computed by the API; nothing is added up.
 */

const moneyFormats = new Map<string, Intl.NumberFormat>();

function moneyFormat(currency: string, signed: boolean): Intl.NumberFormat {
  const key = `${currency}:${signed}`;
  let format = moneyFormats.get(key);
  if (!format) {
    format = new Intl.NumberFormat('pt-BR', { style: 'currency', currency, signDisplay: signed ? 'exceptZero' : 'auto' });
    moneyFormats.set(key, format);
  }

  return format;
}

/** `R$ 3.510,00`; a USD figure as `US$ 10,00`. */
export function formatMoney(value: number, currency = 'BRL'): string {
  return moneyFormat(currency, false).format(value);
}

/** A gain or a loss: `+R$ 297,66`, `-R$ 12,00`. */
export function formatSignedMoney(value: number, currency = 'BRL'): string {
  return moneyFormat(currency, true).format(value);
}

/** A price per unit, to the `numeric(18,8)` column's eighth place, never below the cent. */
export function formatUnitPrice(value: number, currency: string): string {
  return value.toLocaleString('pt-BR', { style: 'currency', currency, minimumFractionDigits: 2, maximumFractionDigits: 8 });
}

export function formatQuantity(value: number): string {
  return value.toLocaleString('pt-BR', { maximumFractionDigits: 8 });
}

/** `0.0927` as `+9,27%`. */
export function formatPercent(fraction: number): string {
  return fraction.toLocaleString('pt-BR', {
    style: 'percent',
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
    signDisplay: 'exceptZero',
  });
}

/** Days since the epoch of a `YYYY-MM-DD`, read as a calendar day: no time zone applies. */
function dayNumber(isoDay: string): number {
  const [year = 1970, month = 1, day = 1] = isoDay.split('-').map(Number);

  return Date.UTC(year, month - 1, day) / 86_400_000;
}

/** Weekdays in `(priceDate, today]`. Holidays are not known here and count as business days. */
export function businessDaysSince(priceDate: string, today: string): number {
  let count = 0;

  for (let day = dayNumber(priceDate) + 1; day <= dayNumber(today); day++) {
    // Day 0 (1970-01-01) was a Thursday.
    const weekday = (day + 4) % 7;
    if (weekday !== 0 && weekday !== 6) {
      count++;
    }
  }

  return count;
}

/** Spec 007: a price older than 3 business days is flagged — the sync may be broken. */
export const STALE_AFTER_BUSINESS_DAYS = 3;

export function isStalePrice(priceDate: string, today: string): boolean {
  return businessDaysSince(priceDate, today) > STALE_AFTER_BUSINESS_DAYS;
}

/** The local calendar day, `YYYY-MM-DD`: a price is stale by the user's calendar. */
export function localToday(): string {
  const now = new Date();

  return [now.getFullYear(), now.getMonth() + 1, now.getDate()].map((part) => String(part).padStart(2, '0')).join('-');
}
