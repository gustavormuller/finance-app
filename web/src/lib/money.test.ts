import { describe, expect, it } from 'vitest';

import { businessDaysSince, formatMoney, formatPercent, formatQuantity, formatUnitPrice, isStalePrice } from './money';

/** Intl separates the symbol with a no-break space; the assertions read better with a plain one. */
const plain = (text: string) => text.replace(/\u00a0/g, ' ');

describe('money formatting', () => {
  it('writes BRL the pt-BR way, and a native currency with its own symbol', () => {
    expect(plain(formatMoney(3510))).toBe('R$ 3.510,00');
    expect(plain(formatMoney(-12.5))).toBe('-R$ 12,50');
    expect(plain(formatMoney(10, 'USD'))).toBe('US$ 10,00');
  });

  it('keeps a unit price to its eighth place, and never below the cent', () => {
    expect(plain(formatUnitPrice(32.1234, 'BRL'))).toBe('R$ 32,1234');
    expect(plain(formatUnitPrice(35.1, 'BRL'))).toBe('R$ 35,10');
    expect(plain(formatUnitPrice(0.00012345, 'USD'))).toBe('US$ 0,00012345');
  });

  it('writes quantities with their fractions only when they have one', () => {
    expect(formatQuantity(1500)).toBe('1.500');
    expect(formatQuantity(0.00123456)).toBe('0,00123456');
  });

  it('writes a fraction as a signed percentage', () => {
    expect(formatPercent(0.0927)).toBe('+9,27%');
    expect(formatPercent(-0.105)).toBe('-10,50%');
    expect(formatPercent(0)).toBe('0,00%');
  });
});

describe('stale prices', () => {
  it('counts the weekdays after the price date, up to today', () => {
    // 2026-09-18 is a Friday; 2026-09-24 a Thursday.
    expect(businessDaysSince('2026-09-18', '2026-09-21')).toBe(1);
    expect(businessDaysSince('2026-09-18', '2026-09-24')).toBe(4);
    expect(businessDaysSince('2026-09-24', '2026-09-24')).toBe(0);
  });

  it('flags a price older than three business days', () => {
    expect(isStalePrice('2026-09-21', '2026-09-24')).toBe(false);
    expect(isStalePrice('2026-09-18', '2026-09-24')).toBe(true);
    // Friday's close read on the Wednesday after a long weekend is not stale yet.
    expect(isStalePrice('2026-09-18', '2026-09-23')).toBe(false);
  });
});
