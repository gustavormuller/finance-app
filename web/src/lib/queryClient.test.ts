import { QueryObserver, type QueryClient } from '@tanstack/react-query';
import { describe, expect, it, vi } from 'vitest';

import { createQueryClient } from './queryClient';

async function mutate(client: QueryClient, mutationFn: () => Promise<unknown>, onSuccess?: () => Promise<unknown>) {
  const mutation = client.getMutationCache().build(client, { mutationFn, ...(onSuccess ? { onSuccess } : {}) });
  await mutation.execute(undefined).catch(() => undefined);
}

describe('createQueryClient', () => {
  it('does not fetch a query again when it is read within 30 seconds', async () => {
    const client = createQueryClient();
    const queryFn = vi.fn(async () => 'value');

    await client.fetchQuery({ queryKey: ['a'], queryFn });
    await client.fetchQuery({ queryKey: ['a'], queryFn });

    expect(queryFn).toHaveBeenCalledTimes(1);
  });

  it('marks every cached query stale when a mutation succeeds, without fetching it then', async () => {
    const client = createQueryClient();
    const queryFn = vi.fn(async () => 'value');
    await client.fetchQuery({ queryKey: ['dashboard', 'summary'], queryFn });
    await client.fetchQuery({ queryKey: ['categories'], queryFn });

    await mutate(client, async () => 'saved');

    expect(client.getQueryState(['dashboard', 'summary'])?.isInvalidated).toBe(true);
    expect(client.getQueryState(['categories'])?.isInvalidated).toBe(true);
    expect(queryFn).toHaveBeenCalledTimes(2);

    // The next read, e.g. a page mounted after the write, fetches again.
    await client.fetchQuery({ queryKey: ['categories'], queryFn });
    expect(queryFn).toHaveBeenCalledTimes(3);
  });

  it('marks every cached query stale when a mutation fails too', async () => {
    const client = createQueryClient();
    await client.fetchQuery({ queryKey: ['transactions'], queryFn: async () => [] });

    await mutate(client, async () => {
      throw new Error('409');
    });

    expect(client.getQueryState(['transactions'])?.isInvalidated).toBe(true);
  });

  it('still refetches a query on screen that the mutation invalidates itself', async () => {
    const client = createQueryClient();
    const queryFn = vi.fn(async () => 'value');
    const observer = new QueryObserver(client, { queryKey: ['transactions'], queryFn });
    const unsubscribe = observer.subscribe(() => undefined);
    await vi.waitFor(() => expect(queryFn).toHaveBeenCalledTimes(1));

    await mutate(client, async () => 'saved', () => client.invalidateQueries({ queryKey: ['transactions'] }));

    expect(queryFn).toHaveBeenCalledTimes(2);
    unsubscribe();
  });
});
