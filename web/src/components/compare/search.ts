import type { CompareCurrency, ComparePeriodKind } from '@/api/finance';
import { comparePeriods } from '@/lib/labels';

/**
 * `/compare`'s search parameters (026, decision 14): the selection and the settings live in
 * the address, so a comparison can be bookmarked and reloaded.
 */
export interface CompareSearch {
  /** The series' keys, comma-separated, in the order chosen. */
  series?: string;
  period?: ComparePeriodKind;
  from?: string;
  to?: string;
  /** Absent is each series in its own currency. */
  currency?: Exclude<CompareCurrency, 'original'>;
  /** Absent is a linear axis. */
  scale?: 'log';
}

/** A change to the address: a parameter given as `undefined` is removed. */
export type CompareSearchPatch = { [Key in keyof CompareSearch]?: CompareSearch[Key] | undefined };

export const MIN_SERIES = 2;
export const MAX_SERIES = 6;

/** With no period and no dates, the API compares the last five years (decision 5). */
export const DEFAULT_PERIOD: ComparePeriodKind = '5y';

/** What `/compare` understands; anything else in the query string is dropped. */
export function validateCompareSearch(search: Record<string, unknown>): CompareSearch {
  return {
    ...(typeof search.series === 'string' && search.series !== '' ? { series: search.series } : {}),
    ...(comparePeriods.includes(search.period as ComparePeriodKind) ? { period: search.period as ComparePeriodKind } : {}),
    ...(typeof search.from === 'string' && search.from !== '' ? { from: search.from } : {}),
    ...(typeof search.to === 'string' && search.to !== '' ? { to: search.to } : {}),
    ...(search.currency === 'BRL' || search.currency === 'USD' ? { currency: search.currency } : {}),
    ...(search.scale === 'log' ? { scale: 'log' as const } : {}),
  };
}

/** The keys in `series`, in order, each once. */
export function seriesKeys(series: string | undefined): string[] {
  return [...new Set((series ?? '').split(',').map((key) => key.trim()).filter(Boolean))];
}

/** The period the page is on: the one in the address, custom when only dates are, else the default. */
export function periodOf(search: CompareSearch): ComparePeriodKind {
  return search.period ?? (search.from || search.to ? 'custom' : DEFAULT_PERIOD);
}

/** `search` with `patch` applied, keeping no empty parameter in the address. */
export function withPatch(search: CompareSearch, patch: CompareSearchPatch): CompareSearch {
  const next: Record<string, unknown> = { ...search, ...patch };

  return validateCompareSearch(Object.fromEntries(Object.entries(next).filter(([, value]) => value !== undefined)));
}
