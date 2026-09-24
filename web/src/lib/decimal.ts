import type { MovementKind } from '@/api/finance';

/**
 * Exact decimal arithmetic for the movement form's live total (007).
 *
 * A browser has no `decimal`, and the preview multiplies money: 0.1 × 3 in floating
 * point is 0.30000000000000004. So what was typed is read into a `bigint` of
 * hundred-millionths — the eight places of the `numeric(18,8)` columns — and the total
 * is rounded once, half to even, to the cent, as the API's `Money` does.
 */
const PLACES = 8;
const SCALE = 10n ** BigInt(PLACES);

/**
 * `1.234,56`, `1234,56` and `1234.56` all read as 1234.56: beside a comma, dots are
 * grouping; alone, a dot is the decimal separator. Returns the canonical text, or null.
 */
function normalize(typed: string): string | null {
  const text = typed.trim();
  const canonical = text.includes(',') ? text.replaceAll('.', '').replace(',', '.') : text;

  return new RegExp(`^-?\\d+(\\.\\d{1,${PLACES}})?$`).test(canonical) ? canonical : null;
}

/** What was typed, in hundred-millionths; null when blank or unreadable. */
export function parseDecimal(typed: string): bigint | null {
  const canonical = normalize(typed);
  if (canonical === null) {
    return null;
  }

  const negative = canonical.startsWith('-');
  const [whole = '0', fraction = ''] = canonical.replace('-', '').split('.');
  const value = BigInt(whole) * SCALE + BigInt(fraction.padEnd(PLACES, '0'));

  return negative ? -value : value;
}

/**
 * The JSON number sent to the API; a blank field is 0. Exact up to fifteen significant
 * digits, which float64 round-trips; the columns allow eighteen (see DEFERRED, 007 · CP4).
 */
export function toApiNumber(typed: string): number {
  const canonical = normalize(typed);

  return canonical === null ? 0 : Number(canonical);
}

/** Integer division rounding half to even. `divisor` is positive. */
function divideToEven(value: bigint, divisor: bigint): bigint {
  const quotient = value / divisor;
  const remainder = value % divisor;
  const twice = (remainder < 0n ? -remainder : remainder) * 2n;
  const away = value < 0n ? -1n : 1n;

  if (twice > divisor || (twice === divisor && quotient % 2n !== 0n)) {
    return quotient + away;
  }

  return quotient;
}

export interface MovementFigures {
  quantity: string;
  unitPrice: string;
  amount: string;
  fees: string;
}

/**
 * The movement's total in cents of its native currency: what a buy costs, what a sell
 * or an income brings in net of fees (spec decision 5; CP2 for income). A split moves
 * no money and has none; neither has a movement whose required field is blank or
 * unreadable. Blank fees are no fees.
 */
export function movementTotal(kind: MovementKind, figures: MovementFigures): bigint | null {
  const fees = figures.fees.trim() === '' ? 0n : parseDecimal(figures.fees);
  if (fees === null || kind === 'Split') {
    return null;
  }

  // Everything at SCALE² so the product of two eight-place figures loses nothing.
  let total: bigint;
  if (kind === 'Dividend' || kind === 'Jcp') {
    const amount = parseDecimal(figures.amount);
    if (amount === null) {
      return null;
    }
    total = (amount - fees) * SCALE;
  } else {
    const quantity = parseDecimal(figures.quantity);
    const price = parseDecimal(figures.unitPrice);
    if (quantity === null || price === null) {
      return null;
    }
    total = quantity * price + (kind === 'Buy' ? fees : -fees) * SCALE;
  }

  return divideToEven(total, (SCALE * SCALE) / 100n);
}
