import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { RouterProvider, createMemoryHistory, createRouter } from '@tanstack/react-router';
import { render } from '@testing-library/react';

import type { Account, AccountBalance, Category, ImportBatch, Transaction } from '@/api/finance';
import { routeTree } from '@/routeTree';
import { stubFetch, type SeenRequest, type StubAnswer } from '@/test-utils';

/**
 * What the accounts page reads, for the tests of `/accounts` and of the import inside
 * it (015). Not a test file: vitest only runs `*.test.tsx`.
 */

export const me = { id: 'u1', email: 'ada@example.com', displayName: 'Ada Lovelace', aiEnabled: true };

export const categories: Category[] = [
  { id: 'cat-food', name: 'Alimentação', kind: 'Expense', parentId: null, createdAt: '' },
  { id: 'cat-other', name: 'Outros', kind: 'Expense', parentId: null, createdAt: '' },
];

export function account(id: string, name: string, change: Partial<Account> = {}): Account {
  return { id, name, type: 'Checking', currency: 'BRL', createdAt: '2026-09-01T00:00:00Z', openingBalance: 0, ...change };
}

export function batch(id: string, of: Account, change: Partial<ImportBatch> = {}): ImportBatch {
  return {
    id,
    accountId: of.id,
    accountName: of.name,
    source: 'Ofx',
    fileName: `${id}.ofx`,
    status: 'Committed',
    rowCount: 3,
    committedCount: 3,
    createdAt: '2026-09-23T14:00:00Z',
    committedAt: '2026-09-23T15:00:00Z',
    ...change,
  };
}

export interface AccountsApi {
  accounts: Account[];
  /** Account id → current balance; an account left out has its opening balance. */
  balances?: Record<string, number>;
  total?: number;
  imports?: ImportBatch[];
  transactions?: Transaction[];
}

type Answer = StubAnswer | undefined;

/**
 * Answers every read the accounts page makes from `state`, which a test may change
 * between requests. `write` answers anything else first; what neither answers fails.
 */
export function stubAccountsApi(state: AccountsApi, write: (request: SeenRequest, url: URL) => Answer = () => undefined) {
  return stubFetch((request) => {
    const url = new URL(request.url, 'http://localhost');
    const answer = write(request, url);

    if (answer || request.method !== 'GET') {
      return answer;
    }

    switch (url.pathname) {
      case '/api/auth/me':
        return { body: me };
      case '/api/accounts':
        return { body: state.accounts };
      case '/api/dashboard/summary': {
        const balances: AccountBalance[] = state.accounts.map((each) => ({
          accountId: each.id,
          name: each.name,
          type: each.type,
          currency: each.currency,
          balance: state.balances?.[each.id] ?? each.openingBalance,
          excludedFromTotal: each.currency !== 'BRL',
        }));

        return { body: { balances, total: state.total ?? 0, month: { income: 0, expense: 0, net: 0 } } };
      }
      case '/api/imports':
        return { body: state.imports ?? [] };
      case '/api/transactions': {
        const items = state.transactions ?? [];

        return { body: { items, page: 1, pageSize: Number(url.searchParams.get('pageSize') ?? 50), total: items.length } };
      }
      case '/api/categories':
        return { body: categories };
      case '/api/csv-templates':
        return { body: [] };
      default:
        return undefined;
    }
  });
}

/** The real route tree over an in-memory history, so the page is tested as it ships. */
export function renderAt(path: string) {
  const router = createRouter({ routeTree, history: createMemoryHistory({ initialEntries: [path] }) });
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  render(
    <QueryClientProvider client={client}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  );

  return router;
}
