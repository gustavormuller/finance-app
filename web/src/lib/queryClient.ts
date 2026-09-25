import { MutationCache, QueryClient } from '@tanstack/react-query';

/** How long a read stays fresh: a page mounted again, or the tab refocused, within it does not refetch. */
const STALE_MS = 30_000;

/**
 * The application's query client (018).
 *
 * With the library's default of no freshness at all, every component that mounted later
 * than the first reader fetched the same data again (`/api/auth/me` three times on a cold
 * load of the dashboard), and every return to a page refetched all of it.
 *
 * Freshness must not outlive a write. Each mutation invalidates what its own screen shows,
 * but not always what other pages derive from it (a new transaction does not name the
 * dashboard's balances). So every mutation, when it settles, succeeded or not, marks every
 * cached query stale without fetching it: whatever is read next is fetched again, and the
 * mutation's own invalidations still refetch what is on screen.
 */
export function createQueryClient(): QueryClient {
  const client: QueryClient = new QueryClient({
    defaultOptions: { queries: { staleTime: STALE_MS } },
    mutationCache: new MutationCache({
      onSettled: () => client.invalidateQueries({ refetchType: 'none' }),
    }),
  });

  return client;
}
