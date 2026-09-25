import { describe, expect, it } from 'vitest';

import type { NetWorthPoint } from '@/api/finance';

import { addMoney, netWorthChanges } from './netWorth';

const point = (month: string, total: number): NetWorthPoint => ({ month, accounts: total, investments: 0, total });

/** Spec 014 web unit test 12. */
describe('netWorthChanges', () => {
  it('compares with the previous month, last December and a year ago, in that order', () => {
    const series = [point('2025-09', 20000), point('2025-12', 27000), point('2026-08', 25000), point('2026-09', 26000)];

    expect(netWorthChanges(series, 26300, '2026-09')).toEqual([
      { key: 'month', label: '1 mês', value: 1300 },
      { key: 'year', label: 'No ano', value: -700 },
      { key: 'twelve', label: '12 meses', value: 6300 },
    ]);
  });

  it('in January, compares 1 mês and No ano with the same December', () => {
    const series = [point('2026-12', 900), point('2027-01', 1000)];

    expect(netWorthChanges(series, 1000, '2027-01')).toEqual([
      { key: 'month', label: '1 mês', value: 100 },
      { key: 'year', label: 'No ano', value: 100 },
    ]);
  });

  it('leaves out a change whose month is not in the series', () => {
    expect(netWorthChanges([point('2026-09', 500)], 500, '2026-09')).toEqual([]);
    expect(netWorthChanges([], 500, '2026-09')).toEqual([]);
  });

  it('subtracts in whole cents, so no change is a hair off or a negative zero', () => {
    const [change] = netWorthChanges([point('2026-08', 0.3), point('2026-09', 0.3)], addMoney(0.1, 0.2), '2026-09');

    expect(addMoney(0.1, 0.2)).toBe(0.3);
    expect(Object.is(change?.value, 0)).toBe(true);
    expect(netWorthChanges([point('2026-08', 1000.1)], 857.1, '2026-09')[0]?.value).toBe(-143);
  });
});
