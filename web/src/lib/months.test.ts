import { afterEach, describe, expect, it, vi } from 'vitest';

import { currentMonth, formatMonth, lastMonths, shiftMonth, shortMonth } from './months';

describe('months', () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  /**
   * The local calendar month, not the UTC one: at 22:00 on 30 September in Brasília
   * it is already October in UTC, and the dashboard must still say September.
   */
  it('reads the current month from the local date', () => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date(2026, 8, 30, 23, 59));

    expect(currentMonth()).toBe('2026-09');
  });

  it.each([
    ['2026-09', -1, '2026-08'],
    ['2026-01', -1, '2025-12'],
    ['2025-12', 1, '2026-01'],
    ['2026-03', -14, '2025-01'],
  ])('shifts %s by %i to %s', (month, delta, expected) => {
    expect(shiftMonth(month, delta)).toBe(expected);
  });

  it('writes a month out in Portuguese', () => {
    expect(formatMonth('2026-09')).toBe('setembro de 2026');
    expect(shortMonth('2026-01')).toBe('jan/26');
  });

  /**
   * The API's series ends at its UTC month, which can be a month past the local one.
   * Asking for one extra and keeping those up to the local month absorbs that.
   */
  it('keeps the last N months up to the local month', () => {
    const series = ['2026-08', '2026-09', '2026-10'].map((month) => ({ month }));

    expect(lastMonths(series, '2026-09', 2).map((entry) => entry.month)).toEqual(['2026-08', '2026-09']);
    expect(lastMonths(series, '2026-10', 2).map((entry) => entry.month)).toEqual(['2026-09', '2026-10']);
  });
});
