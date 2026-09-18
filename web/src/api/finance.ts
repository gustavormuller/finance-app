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
  importBatchId?: string;
  page?: number;
  pageSize?: number;
}

// ---- 004: import ------------------------------------------------------------

export type ImportSource = 'Ofx' | 'Csv';

export type ImportBatchStatus = 'Staged' | 'Committed';

export type StagedRowStatus = 'Ready' | 'Duplicate' | 'Invalid';

export type SignMode = 'Signed' | 'SignedInverted' | 'DebitCredit';

/** The cultures the API's amount parser knows. Case-sensitive, stored as written. */
export type AmountCulture = 'pt-BR' | 'en-US';

export interface CsvPreview {
  /** The first row of the table, header or not; the mapping step decides. */
  headers: string[];
  sampleRows: string[][];
  delimiter: string;
  skippedRows: number;
  rowCount: number;
}

/** The column mapping, as sent inline with an upload and as saved in a template. */
export interface CsvMappingInput {
  delimiter: string;
  hasHeader: boolean;
  culture: AmountCulture;
  dateFormat: string;
  signMode: SignMode;
  dateColumn: string;
  amountColumn: string | null;
  debitColumn: string | null;
  creditColumn: string | null;
  /** Comma-separated references, joined in order. */
  descriptionColumns: string;
}

export interface CsvTemplate extends CsvMappingInput {
  id: string;
  name: string;
  createdAt: string;
}

export interface CsvTemplateInput extends CsvMappingInput {
  name: string;
}

export interface ImportUpload {
  file: File;
  accountId: string;
  source: ImportSource;
  templateId?: string;
  mapping?: CsvMappingInput;
}

export interface ImportUploadResult {
  batchId: string;
  rowCount: number;
  ready: number;
  duplicates: number;
  invalid: number;
}

export interface ImportBatch {
  id: string;
  accountId: string;
  accountName: string;
  source: ImportSource;
  fileName: string;
  status: ImportBatchStatus;
  rowCount: number;
  committedCount: number | null;
  createdAt: string;
  committedAt: string | null;
}

/**
 * `amount` is a number for the same reason `Transaction.amount` is, and is only
 * ever displayed here, never computed with.
 */
export interface StagedRow {
  id: string;
  rowNumber: number;
  date: string | null;
  amount: number | null;
  currency: string | null;
  rawDescription: string;
  externalId: string | null;
  categoryId: string | null;
  status: StagedRowStatus;
  included: boolean;
  issues: string[];
}

export interface StagedRowCounts {
  ready: number;
  duplicates: number;
  invalid: number;
  included: number;
}

export interface StagedRowPage {
  items: StagedRow[];
  page: number;
  pageSize: number;
  total: number;
}

export interface ImportBatchDetail {
  batch: ImportBatch;
  counts: StagedRowCounts;
  rows: StagedRowPage;
}

export interface StagedRowPatch {
  categoryId?: string;
  include?: boolean;
}

export interface ImportRowsQuery {
  status?: StagedRowStatus;
  page?: number;
  pageSize?: number;
}

export interface CommitResult {
  committed: number;
  skipped: number;
}

export interface UndoResult {
  deleted: number;
}

/**
 * A refusal from the API, with the offending fields a 400 names and, for the 409 an
 * upload gets while another import is open, the id of that import.
 */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    message: string,
    readonly fields: Record<string, string[]> = {},
    readonly openBatchId: string | null = null,
  ) {
    super(message);
    this.name = 'ApiError';
  }
}

async function request<T>(url: string, init?: RequestInit): Promise<T> {
  // A FormData body sets its own multipart boundary; only a JSON string needs the
  // header, and forcing it onto multipart would break the upload.
  const response = await fetch(url, {
    ...init,
    credentials: 'same-origin',
    headers: typeof init?.body === 'string' ? { 'Content-Type': 'application/json' } : {},
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
    typeof problem.openBatchId === 'string' ? problem.openBatchId : null,
  );
}

function searchParams(query: object): string {
  const search = new URLSearchParams();

  for (const [key, value] of Object.entries(query)) {
    if (value !== undefined && value !== '') {
      search.set(key, String(value));
    }
  }

  return search.toString();
}

/** The multipart body of an upload: the file, then every mapping field that is set. */
function uploadBody(upload: ImportUpload): FormData {
  const body = new FormData();

  body.append('file', upload.file, upload.file.name);
  body.append('accountId', upload.accountId);
  body.append('source', upload.source);

  if (upload.templateId) {
    body.append('templateId', upload.templateId);
  }

  for (const [key, value] of Object.entries(upload.mapping ?? {})) {
    if (value !== null && value !== undefined) {
      body.append(key, String(value));
    }
  }

  return body;
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

  listTransactions: (query: TransactionQuery) =>
    request<TransactionPage>(`/api/transactions?${searchParams(query)}`),

  createTransaction: (input: TransactionInput) =>
    request<Transaction>('/api/transactions', { method: 'POST', body: JSON.stringify(input) }),

  updateTransaction: (id: string, input: TransactionInput) =>
    request<Transaction>(`/api/transactions/${id}`, { method: 'PUT', body: JSON.stringify(input) }),

  deleteTransaction: (id: string) =>
    request<void>(`/api/transactions/${id}`, { method: 'DELETE' }),

  previewCsv: (file: File, delimiter?: string) => {
    const body = new FormData();
    body.append('file', file, file.name);

    if (delimiter) {
      body.append('delimiter', delimiter);
    }

    return request<CsvPreview>('/api/imports/preview-csv', { method: 'POST', body });
  },

  uploadImport: (upload: ImportUpload) =>
    request<ImportUploadResult>('/api/imports', { method: 'POST', body: uploadBody(upload) }),

  listImports: () => request<ImportBatch[]>('/api/imports'),

  getImport: (id: string, query: ImportRowsQuery = {}) =>
    request<ImportBatchDetail>(`/api/imports/${id}?${searchParams(query)}`),

  patchImportRow: (batchId: string, rowId: string, patch: StagedRowPatch) =>
    request<StagedRow>(`/api/imports/${batchId}/rows/${rowId}`, {
      method: 'PATCH',
      body: JSON.stringify(patch),
    }),

  commitImport: (id: string) =>
    request<CommitResult>(`/api/imports/${id}/commit`, { method: 'POST' }),

  discardImport: (id: string) => request<void>(`/api/imports/${id}`, { method: 'DELETE' }),

  undoImport: (id: string) => request<UndoResult>(`/api/imports/${id}/undo`, { method: 'POST' }),

  listCsvTemplates: () => request<CsvTemplate[]>('/api/csv-templates'),

  createCsvTemplate: (input: CsvTemplateInput) =>
    request<CsvTemplate>('/api/csv-templates', { method: 'POST', body: JSON.stringify(input) }),

  deleteCsvTemplate: (id: string) =>
    request<void>(`/api/csv-templates/${id}`, { method: 'DELETE' }),
};
