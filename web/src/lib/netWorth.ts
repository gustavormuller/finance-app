import type { NetWorthPoint } from '@/api/finance';
import { shiftMonth } from '@/lib/months';

/**
 * The hero's net-worth arithmetic. The API's figures carry two decimal places, so
 * they are added and subtracted in whole cents: in floating point 0.1 + 0.2 is a hair
 * off 0.3, and a hair below zero would print as "−0,00".
 */
const cents = (value: number) => Math.round(value * 100);

/** `a + b`, to the cent. */
export function addMoney(a: number, b: number): number {
  return (cents(a) + cents(b)) / 100;
}

type NetWorthChangeKey = 'month' | 'year' | 'twelve';

export interface NetWorthChange {
  key: NetWorthChangeKey;
  label: string;
  value: number;
}

/** The chips in the order they are drawn, each with the month-end it compares with. */
const CHANGES: { key: NetWorthChangeKey; label: string; since: (through: string) => string }[] = [
  { key: 'month', label: '1 mês', since: (through) => shiftMonth(through, -1) },
  { key: 'year', label: 'No ano', since: (through) => shiftMonth(`${through.slice(0, 4)}-01`, -1) },
  { key: 'twelve', label: '12 meses', since: (through) => shiftMonth(through, -12) },
];

/**
 * How far `current` is from the total at the end of the previous month, of last
 * December and of the same month a year ago (`through` being this month). A change whose
 * month is not in the series is left out: the history is too short to say.
 */
export function netWorthChanges(series: NetWorthPoint[], current: number, through: string): NetWorthChange[] {
  const totals = new Map(series.map((point) => [point.month, point.total]));

  return CHANGES.flatMap(({ key, label, since }) => {
    const then = totals.get(since(through));

    return then === undefined ? [] : [{ key, label, value: (cents(current) - cents(then)) / 100 }];
  });
}
