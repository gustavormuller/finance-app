import { useQuery } from '@tanstack/react-query';

import { api, type CompareQuery, type CompareSeries } from '@/api/finance';
import { retryUnlessRefused } from '@/components/returns/queries';
import { seriesColour } from '@/lib/chart';
import { compareBenchmarks } from '@/lib/labels';

export const COMPARE = ['compare'] as const;

/**
 * A change of period, currency or scale keeps the last answer on screen until the next one
 * arrives; a change of series does not, since the old lines would carry the new names.
 */
export function useComparison(query: CompareQuery, enabled: boolean) {
  return useQuery({
    queryKey: [...COMPARE, query],
    queryFn: () => api.compare(query),
    enabled,
    placeholderData: (previous) =>
      previous && previous.series.map((series) => series.key).join(',') === query.series.join(',') ? previous : undefined,
    retry: retryUnlessRefused,
  });
}

/** What a series is called on screen: its ticker or a benchmark's name, and what it is. */
export interface SeriesLabel {
  label: string;
  detail: string | null;
}

const BENCHMARK = 'benchmark:';

/** A benchmark's code from its key; null for an asset. */
export function benchmarkCode(key: string): string | null {
  return key.startsWith(BENCHMARK) ? key.slice(BENCHMARK.length) : null;
}

export function benchmarkLabel(code: string): SeriesLabel {
  const known = compareBenchmarks.find((benchmark) => benchmark.code === code);

  return { label: known?.name ?? code, detail: known?.description ?? null };
}

/** A series as the API describes it. */
export function labelOf(series: CompareSeries): SeriesLabel {
  if (series.kind === 'benchmark') {
    return benchmarkLabel(series.code ?? '');
  }

  return { label: series.ticker ?? 'Ativo', detail: series.name && series.name !== series.ticker ? series.name : null };
}

/**
 * Up to six lines: the chart tokens, which differ in lightness as well as hue, and from the
 * fourth on a dash too, so a line is told apart without its colour (decision 18).
 */
const DASHES = ['', '', '', '6 3', '2 3', '10 3 2 3'];

export const lineColour = seriesColour;

export const lineDash = (index: number) => DASHES[index] ?? '';
