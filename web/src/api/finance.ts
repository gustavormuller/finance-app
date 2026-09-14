/**
 * The 003 API surface, hand-written.
 *
 * ARCHITECTURE.md's stack table calls for these types to be generated from OpenAPI
 * with openapi-typescript. Nothing exposes an OpenAPI document yet and standing that
 * up is its own piece of work, not 003's — so these are written by hand and the
 * integration tests are what keep them honest, since they declare the same shapes
 * independently and assert against the real endpoints.
 */

export type AccountType = 'Checking' | 'Savings' | 'CreditCard' | 'Cash' | 'Investment';

export type CategoryKind = 'Income' | 'Expense';

export interface Account {
  id: string;
  name: string;
  type: AccountType;
  currency: string;
  createdAt: string;
}

export interface Category {
  id: string;
  name: string;
  kind: CategoryKind;
  parentId: string | null;
  createdAt: string;
}

/**
 * `amount` is signed: negative leaves, positive arrives.
 *
 * It is a JavaScript number, which is a float64 — the one place in the system that
 * is not a decimal, and unavoidable in a browser (ADR-002 says as much). It is safe
 * here because the server is authoritative: every amount is rounded and stored as
 * `numeric(18,2)` there, and nothing client-side does arithmetic on money beyond
 * applying a sign. Any future totalling belongs on the API, not here.
 */
export interface Transaction {
  id: string;
  accountId: string;
  accountName: string;
  categoryId: string;
  categoryName: string;
  amount: number;
  currency: string;
  date: string;
  description: string;
  createdAt: string;
}

export interface TransactionPage {
  items: Transaction[];
  page: number;
  pageSize: number;
  total: number;
}

export interface TransactionInput {
  accountId: string;
  categoryId: string;
  amount: number;
  currency: string;
  date: string;
  description: string;
}

export interface AccountInput {
  name: string;
  type: AccountType;
  currency: string;
}

export interface CategoryInput {
  name: string;
  kind: CategoryKind;
  parentId: string | null;
}

export interface TransactionQuery {
  from?: string;
  to?: string;
  accountId?: string;
  categoryId?: string;
  page?: number;
  pageSize?: number;
}

/** A 400 from the API, with the offending fields the endpoints name. */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    message: string,
    readonly fields: Record<string, string[]> = {},
  ) {
    super(message);
    this.name = 'ApiError';
  }
}

async function request<T>(url: string, init?: RequestInit): Promise<T> {
  const response = await fetch(url, {
    ...init,
    credentials: 'same-origin',
    headers: init?.body ? { 'Content-Type': 'application/json' } : {},
  });

  if (response.ok) {
    return response.status === 204 ? (undefined as T) : ((await response.json()) as T);
  }

  // The API answers every refusal as problem details. `errors` is present on a 400
  // naming fields; `detail` carries the readable sentence a 409 exists to give.
  const problem = await response.json().catch(() => ({}) as Record<string, unknown>);

  throw new ApiError(
    response.status,
    (problem.detail as string) ?? (problem.title as string) ?? `Request failed (${response.status})`,
    (problem.errors as Record<string, string[]>) ?? {},
  );
}

export const api = {
  listAccounts: () => request<Account[]>('/api/accounts'),

  createAccount: (input: AccountInput) =>
    request<Account>('/api/accounts', { method: 'POST', body: JSON.stringify(input) }),

  updateAccount: (id: string, input: AccountInput) =>
    request<Account>(`/api/accounts/${id}`, { method: 'PUT', body: JSON.stringify(input) }),

  deleteAccount: (id: string) => request<void>(`/api/accounts/${id}`, { method: 'DELETE' }),

  listCategories: () => request<Category[]>('/api/categories'),

  createCategory: (input: CategoryInput) =>
    request<Category>('/api/categories', { method: 'POST', body: JSON.stringify(input) }),

  updateCategory: (id: string, input: CategoryInput) =>
    request<Category>(`/api/categories/${id}`, { method: 'PUT', body: JSON.stringify(input) }),

  deleteCategory: (id: string) => request<void>(`/api/categories/${id}`, { method: 'DELETE' }),

  listTransactions: (query: TransactionQuery) => {
    const search = new URLSearchParams();

    for (const [key, value] of Object.entries(query)) {
      if (value !== undefined && value !== '') {
        search.set(key, String(value));
      }
    }

    return request<TransactionPage>(`/api/transactions?${search}`);
  },

  createTransaction: (input: TransactionInput) =>
    request<Transaction>('/api/transactions', { method: 'POST', body: JSON.stringify(input) }),

  updateTransaction: (id: string, input: TransactionInput) =>
    request<Transaction>(`/api/transactions/${id}`, { method: 'PUT', body: JSON.stringify(input) }),

  deleteTransaction: (id: string) =>
    request<void>(`/api/transactions/${id}`, { method: 'DELETE' }),
};
