import { useQuery } from '@tanstack/react-query';

import { api } from '@/api/finance';

/**
 * Every investments query sits under `['investments']`: a movement write changes the positions,
 * the summary and the daily series at once, so one invalidation refreshes them all.
 */
export const INVESTMENTS = ['investments'] as const;

export function usePositions() {
  return useQuery({ queryKey: [...INVESTMENTS, 'positions'], queryFn: api.listPositions });
}

export function usePortfolioSummary() {
  return useQuery({ queryKey: [...INVESTMENTS, 'summary'], queryFn: api.portfolioSummary });
}

export function useMovements(assetId: string) {
  return useQuery({ queryKey: [...INVESTMENTS, 'movements', assetId], queryFn: () => api.listMovements(assetId) });
}

export function useDaily(assetId: string) {
  return useQuery({ queryKey: [...INVESTMENTS, 'daily', assetId], queryFn: () => api.listDaily(assetId) });
}
