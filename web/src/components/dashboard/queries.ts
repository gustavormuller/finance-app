import { keepPreviousData, useQuery } from '@tanstack/react-query';

import { api, type BreakdownKind } from '@/api/finance';
import { lastMonths } from '@/lib/months';

/** The series the chart shows. */
export const SERIES_MONTHS = 12;

/**
 * Every dashboard query sits under `['dashboard']`, so one invalidation refreshes the
 * whole page. None sets a stale time: the page is visited after writes elsewhere, and
 * the default refetch-on-mount is what makes it current.
 */
export function useSummary(month: string) {
  return useQuery({
    queryKey: ['dashboard', 'summary', month],
    queryFn: () => api.dashboardSummary(month),
    // Keeps the numbers on screen while the previous month loads, instead of a flash.
    placeholderData: keepPreviousData,
  });
}

/**
 * The last {@link SERIES_MONTHS} months up to `through`, the local month. The API
 * ends its series at the UTC month and takes no month parameter, so one extra month
 * is asked for and a month that has not begun locally is dropped.
 */
export function useMonthly(through: string) {
  return useQuery({
    queryKey: ['dashboard', 'monthly', SERIES_MONTHS + 1],
    queryFn: () => api.dashboardMonthly(SERIES_MONTHS + 1),
    select: (series) => lastMonths(series, through, SERIES_MONTHS),
  });
}

/** 014: the month-ends the hero's sparkline spans, which covers the 12 meses change too. */
export const NET_WORTH_MONTHS = 24;

/** The net-worth series up to `through`, the local month, read as {@link useMonthly} reads its own. */
export function useNetWorth(through: string) {
  return useQuery({
    queryKey: ['dashboard', 'net-worth', NET_WORTH_MONTHS + 1],
    queryFn: () => api.dashboardNetWorth(NET_WORTH_MONTHS + 1),
    select: (series) => lastMonths(series, through, NET_WORTH_MONTHS),
  });
}

export function useByCategory(month: string, kind: BreakdownKind) {
  return useQuery({
    queryKey: ['dashboard', 'by-category', month, kind],
    queryFn: () => api.dashboardByCategory(month, kind),
  });
}

/** Shares the transactions page's key prefix, so its writes refresh this too. */
export function useRecent(count: number) {
  const query = { page: 1, pageSize: count };

  return useQuery({
    queryKey: ['transactions', query],
    queryFn: () => api.listTransactions(query),
  });
}
