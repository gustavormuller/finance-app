import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';

import { api } from '@/api/finance';
import { useSummary } from '@/components/dashboard/queries';
import { currentMonth } from '@/lib/months';

/** Under `['accounts']`, as the transactions page reads them, so one invalidation serves both. */
export function useAccounts() {
  return useQuery({ queryKey: ['accounts'], queryFn: api.listAccounts });
}

/** Every batch of the user's; each account filters its own (015, decision 5). */
export function useImports() {
  return useQuery({ queryKey: ['imports'], queryFn: api.listImports });
}

/**
 * The dashboard's summary, for its `balances` and `total`. A balance is all-time, so
 * the month only has to be one the dashboard also asks for: sharing its cache entry
 * means `['dashboard']` invalidations refresh both pages.
 */
export function useBalances() {
  const [month] = useState(currentMonth);

  return useSummary(month);
}
