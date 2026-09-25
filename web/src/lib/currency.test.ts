import { act, renderHook } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';

import type { AssetReturns, Returns } from '@/api/finance';

import { brlToUsd, chooseCurrency, readCurrencyChoice, returnsInUsd, useCurrencyChoice } from './currency';
import { formatMoney, formatSignedMoney } from './money';

const plain = (text: string) => text.replace(/\u00a0/g, ' ');

/** Spec 016 web test 3. */
describe('brlToUsd', () => {
  it('divides by the rate, to the cent', () => {
    expect(brlToUsd(1000, 0.05)).toBe(20000);
    expect(brlToUsd(226692.85, 5.3)).toBe(42772.24);
    expect(brlToUsd(3510, 5.3012)).toBe(662.11);
    expect(brlToUsd(0, 5.3)).toBe(0);
  });

  it('keeps a loss negative', () => {
    expect(brlToUsd(-460.14, 5.3)).toBe(-86.82);
  });

  it('rounds a half cent to even, once, on the exact quotient', () => {
    expect(brlToUsd(0.25, 2)).toBe(0.12);
    expect(brlToUsd(0.03, 2)).toBe(0.02);
    // 0.12 / 1.6 is 0.075, but 0.07499999999999999 in float64: rounding the float loses the cent.
    expect((0.12 / 1.6).toFixed(2)).toBe('0.07');
    expect(brlToUsd(0.12, 1.6)).toBe(0.08);
  });

  it('refuses a rate that is not positive', () => {
    expect(() => brlToUsd(10, 0)).toThrow(RangeError);
  });
});

/** Spec 016 web test 6. */
it('prints a dollar figure as US$', () => {
  expect(plain(formatMoney(brlToUsd(1000, 0.05), 'USD'))).toBe('US$ 20.000,00');
  expect(plain(formatSignedMoney(-86.82, 'USD'))).toBe('-US$ 86,82');
});

const threeYears: Returns = {
  period: { from: '2023-09-01', to: '2026-08-31', days: 1096 },
  twr: { total: 0.8423, annualised: 0.2255 },
  xirr: 0.1414,
  timingEffect: -0.0841,
  benchmarks: {
    CDI: { total: 0.8441, annualised: 0.2261 },
    IPCA6: null,
    IVVB11: { total: 0.4697, annualised: 0.1369 },
    SELIC: { total: 0.8452, annualised: 0.2264 },
    USDBRL: { total: 0.01, annualised: 0.0033 },
  },
  series: [
    { date: '2023-08-31', portfolio: 100, CDI: 100, IVVB11: 100, SELIC: 100, USDBRL: 100 },
    { date: '2025-02-28', portfolio: 150, CDI: 140, IVVB11: 120, SELIC: 140.5, USDBRL: 125 },
    { date: '2026-08-31', portfolio: 184.23, CDI: 184.41, IVVB11: 146.97, SELIC: 184.52, USDBRL: 101 },
  ],
};

/** Spec 016 web test 4. */
describe('returnsInUsd', () => {
  it('divides every index by the dollar\'s, so the TWR is the last point over 100', () => {
    const usd = returnsInUsd(threeYears)!;

    // 184.23 / 101 = 1.82405940594...
    expect(usd.twr!.total).toBe(0.8240594059);
    expect(usd.benchmarks.CDI!.total).toBe(0.8258415842);
    expect(usd.benchmarks.IVVB11!.total).toBe(0.4551485149);
    expect(usd.benchmarks.IPCA6).toBeNull();
    expect(usd.series.map((point) => point.portfolio)).toEqual([100, 120, 182.405941]);
    expect(usd.series[1]).toEqual({ date: '2025-02-28', portfolio: 120, CDI: 112, IVVB11: 96, SELIC: 112.4 });
  });

  it('drops the dollar, which is the unit, and keeps XIRR and the timing effect as sent', () => {
    const usd = returnsInUsd(threeYears)!;

    expect(Object.keys(usd.benchmarks)).toEqual(['CDI', 'IPCA6', 'IVVB11', 'SELIC']);
    expect(usd.series.every((point) => !('USDBRL' in point))).toBe(true);
    expect(usd.xirr).toBe(0.1414);
    expect(usd.timingEffect).toBe(-0.0841);
    expect(usd.period).toEqual(threeYears.period);
  });

  it('annualises over the period\'s days', () => {
    const usd = returnsInUsd(threeYears)!;

    // 1.8240594059 ^ (365 / 1096) - 1
    expect(usd.twr!.annualised).toBeCloseTo(0.2216, 4);
    expect(usd.benchmarks.CDI!.annualised).toBeCloseTo(Math.pow(1.8258415842, 365 / 1096) - 1, 9);
  });

  it('keeps an asset\'s FX split as sent', () => {
    const asset: AssetReturns = { ...threeYears, fx: { native: 0.1, fx: 0.1, total: 0.21 } };

    expect(returnsInUsd(asset)!.fx).toEqual({ native: 0.1, fx: 0.1, total: 0.21 });
  });

  it('is null without a dollar index to divide by, or with nothing held', () => {
    const noDollar: Returns = {
      ...threeYears,
      benchmarks: { ...threeYears.benchmarks, USDBRL: null },
      series: threeYears.series.map((point) => ({ date: point.date, portfolio: point.portfolio, CDI: point.CDI! })),
    };

    expect(returnsInUsd(noDollar)).toBeNull();
    expect(returnsInUsd({ ...threeYears, period: null, twr: null, series: [] })).toBeNull();
  });
});

/** Spec 016 web test 5. */
describe('the currency choice', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    act(() => chooseCurrency('BRL'));
    localStorage.clear();
  });

  it('is R$ by default, and a stored US$ is read back', () => {
    expect(readCurrencyChoice()).toBe('BRL');

    localStorage.setItem('currency', 'USD');
    expect(readCurrencyChoice()).toBe('USD');

    localStorage.setItem('currency', 'EUR');
    expect(readCurrencyChoice()).toBe('BRL');
  });

  it('is stored when chosen, and every reader follows', () => {
    const first = renderHook(() => useCurrencyChoice());
    const second = renderHook(() => useCurrencyChoice());
    expect(first.result.current[0]).toBe('BRL');

    act(() => first.result.current[1]('USD'));

    expect(localStorage.getItem('currency')).toBe('USD');
    expect(first.result.current[0]).toBe('USD');
    expect(second.result.current[0]).toBe('USD');
  });

  it('is R$ when storage cannot be read, and still switches when it cannot be written', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    const { result } = renderHook(() => useCurrencyChoice());
    expect(result.current[0]).toBe('BRL');

    act(() => result.current[1]('USD'));

    expect(result.current[0]).toBe('USD');
  });
});
