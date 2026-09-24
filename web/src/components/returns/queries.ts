import { useQuery } from '@tanstack/react-query';

import { ApiError, api, type ReturnsQuery } from '@/api/finance';
import { INVESTMENTS } from '@/components/investments/queries';

/**
 * Under `['investments']`, so a movement write refreshes the returns with the positions.
 * The period is part of the key: changing it is a new request (spec web test 35).
 */
export const RETURNS = [...INVESTMENTS, 'returns'] as const;

/** A 400 or 404 says the same thing on every try, and its message should show at once. */
export function retryUnlessRefused(failures: number, error: Error): boolean {
  return !(error instanceof ApiError && error.status < 500) && failures < 3;
}

export function usePortfolioReturns(query: ReturnsQuery) {
  return useQuery({
    queryKey: [...RETURNS, 'portfolio', query],
    queryFn: () => api.portfolioReturns(query),
    retry: retryUnlessRefused,
  });
}

/** The messages a 400 names by field, pt-BR as sent; empty for any other failure. */
export function fieldErrors(error: Error | null): Record<string, string[]> {
  return error instanceof ApiError ? error.fields : {};
}
