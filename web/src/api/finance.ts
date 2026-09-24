/**
 * The 003 API surface, hand-written.
 *
 * ARCHITECTURE.md's stack table calls for these types to be generated from OpenAPI
 * with openapi-typescript. Nothing exposes an OpenAPI document yet and standing that
 * up is its own piece of work, not 003's — so these are written by hand and the
 * integration tests are what keep them honest, since they declare the same shapes
 * independently and assert against the real endpoints.
 */

import type { AuthenticatedUser } from '@/auth/useMe';

export type AccountType = 'Checking' | 'Savings' | 'CreditCard' | 'Cash' | 'Investment';

/** `Transfer` (005) moves money between the user's own accounts: any sign, never income or expense. */
export type CategoryKind = 'Income' | 'Expense' | 'Transfer';

export interface Account {
  id: string;
  name: string;
  type: AccountType;
  currency: string;
  createdAt: string;
  /** The balance before every recorded transaction (005). A number for the reason `Transaction.amount` is. */
  openingBalance: number;
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
  /** Omitted on create means 0; omitted on update keeps the stored value. */
  openingBalance?: number;
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

/**
 * Which rung of the cascade chose a staged row's category (009): `Default` is the sign
 * default, the only rows "Sugerir com IA" sends; `Ai` came from that; `User` was picked
 * by hand in the preview.
 */
export type CategorySource = 'None' | 'History' | 'Default' | 'Ai' | 'User';

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
  categorySource: CategorySource;
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

/** `suggested`: rows the AI moved off the default; `skipped`: rows sent and left as they were. */
export interface SuggestResult {
  suggested: number;
  skipped: number;
}

// ---- 005: dashboard -----------------------------------------------------------

/**
 * The dashboard's money is signed as stored — `expense` and every Expense category
 * `amount` are negative — and is only displayed here, never computed with, for the
 * reason `Transaction.amount` gives.
 */
export interface AccountBalance {
  accountId: string;
  name: string;
  type: AccountType;
  currency: string;
  balance: number;
  /** True for every non-BRL account: listed, but not added to `total`. */
  excludedFromTotal: boolean;
}

export interface MonthSummary {
  income: number;
  expense: number;
  net: number;
}

export interface DashboardSummary {
  balances: AccountBalance[];
  total: number;
  month: MonthSummary;
}

export interface MonthTotals {
  /** `YYYY-MM`. */
  month: string;
  income: number;
  expense: number;
}

export interface CategoryTotal {
  categoryId: string;
  name: string;
  amount: number;
  /** Fraction of the month's total for the kind, 4 places, positive. */
  share: number;
}

/** The kinds the breakdown accepts; a Transfer is neither and the API refuses it. */
export type BreakdownKind = Extract<CategoryKind, 'Income' | 'Expense'>;

// ---- 006: market data -----------------------------------------------------------

export type MarketAssetClass = 'StockBr' | 'Fii' | 'EtfBr' | 'Bdr' | 'StockUs' | 'Crypto';

export type ProviderKind = 'Brapi' | 'CoinGecko' | 'TwelveData';

export type SyncTrigger = 'Scheduled' | 'Manual';

export type SyncRunStatus = 'Running' | 'Succeeded' | 'PartialFailure' | 'Failed';

/** A shared catalogue entry: every user sees and registers into the same one. */
export interface MarketAsset {
  id: string;
  ticker: string;
  name: string;
  class: MarketAssetClass;
  currency: string;
  provider: ProviderKind;
  providerSymbol: string;
  isActive: boolean;
  lastSyncedAt: string | null;
  createdAt: string;
}

export interface MarketAssetInput {
  ticker: string;
  /** Omitted or blank, the API uses the ticker. */
  name?: string;
  class: MarketAssetClass;
  provider: ProviderKind;
  providerSymbol: string;
  currency: string;
}

/** An item that failed, and why; `error` is already pt-BR. */
export interface SyncFailure {
  item: string;
  error: string;
}

export interface ProviderSyncSummary {
  rowsWritten: number;
  itemsSynced: number;
  itemsFailed: number;
  error: string | null;
  failures: SyncFailure[];
}

export interface SyncRun {
  id: string;
  startedAt: string;
  finishedAt: string | null;
  trigger: SyncTrigger;
  status: SyncRunStatus;
  /** Keyed by provider name: a `ProviderKind` member, or `Bcb` for the benchmark series. */
  summary: Record<string, ProviderSyncSummary>;
}

// ---- 007: investments -----------------------------------------------------------

export type MovementKind = 'Buy' | 'Sell' | 'Dividend' | 'Jcp' | 'Split';

/**
 * One held asset (`GET /api/investments/assets`). Figures are numbers for the reason
 * `Transaction.amount` gives, and are only displayed. The valuation, `price` through
 * `unrealisedPct`, is null until the asset has a daily row; `realisedBrl` and
 * `dividendsBrl` are null for a USD asset while no USDBRL rate exists.
 */
export interface Position {
  assetId: string;
  ticker: string;
  name: string;
  class: MarketAssetClass;
  currency: string;
  nickname: string | null;
  quantity: number;
  averageCost: number;
  price: number | null;
  priceDate: string | null;
  valueBrl: number | null;
  costBasisBrl: number | null;
  unrealisedBrl: number | null;
  unrealisedPct: number | null;
  realisedBrl: number | null;
  dividendsBrl: number | null;
}

export interface PortfolioSummary {
  totalBrl: number;
  totalCostBrl: number;
  unrealisedBrl: number;
}

/** Either a catalogue entry already there, or 006's registration body. */
export type AddAssetInput = { marketAssetId: string } | MarketAssetInput;

export interface Movement {
  id: string;
  assetId: string;
  date: string;
  kind: MovementKind;
  quantity: number;
  unitPrice: number;
  amount: number;
  fees: number;
  currency: string;
  notes: string | null;
  createdAt: string;
}

/** `currency` is omitted: the API takes the asset's. */
export interface MovementInput {
  date: string;
  kind: MovementKind;
  quantity: number;
  unitPrice: number;
  amount: number;
  fees: number;
  notes: string | null;
}

export interface DailyRow {
  date: string;
  quantity: number;
  averageCost: number;
  price: number;
  priceDate: string;
  fxRate: number;
  valueBrl: number;
  costBasisBrl: number;
}

// ---- 008: returns ---------------------------------------------------------------

/** The `period` query value; `custom` takes `from`/`to` (`YYYY-MM-DD`). */
export type ReturnsPeriodKind = 'inception' | 'ytd' | '12m' | 'custom';

export interface ReturnsQuery {
  period: ReturnsPeriodKind;
  from?: string;
  to?: string;
}

/** `days` runs from the base day (base 100) to `to`. */
export interface ReturnsPeriod {
  from: string;
  to: string;
  days: number;
}

/** Rates are fractions (`0.1234` is 12,34%). `annualised` is null past `decimal`'s range. */
export interface PeriodReturn {
  total: number;
  annualised: number | null;
}

export interface FxSplit {
  native: number;
  fx: number;
  total: number;
}

/**
 * One point of the base-100 chart: `date`, `portfolio`, and a key per benchmark code
 * that could anchor. A benchmark that is null in `benchmarks` has no key here.
 */
export interface ReturnsPoint {
  date: string;
  portfolio: number;
  [code: string]: number | string;
}

/**
 * `GET /api/returns/portfolio`. Everything but `benchmarks` and `series` is null when
 * nothing was held in the period. `benchmarks` is keyed by code, ordered by code.
 */
export interface Returns {
  period: ReturnsPeriod | null;
  twr: PeriodReturn | null;
  xirr: number | null;
  timingEffect: number | null;
  benchmarks: Record<string, PeriodReturn | null>;
  series: ReturnsPoint[];
}

/** `GET /api/returns/assets/{id}`: the same, and the FX split, null for a BRL asset. */
export interface AssetReturns extends Returns {
  fx: FxSplit | null;
}

// ---- 009: AI -------------------------------------------------------------------

export type AnalysisStatus = 'Pending' | 'Running' | 'Completed' | 'Failed';

/**
 * One month's analysis. `content` is the provider's markdown, only when `Completed`, and
 * untrusted: render it with `Markdown`, never as HTML. `error` is pt-BR, only when `Failed`.
 */
export interface AiAnalysis {
  id: string;
  month: string;
  status: AnalysisStatus;
  content: string | null;
  error: string | null;
  promptVersion: string;
  createdAt: string;
  startedAt: string | null;
  completedAt: string | null;
}

/** The month's AI spend against the cap. `spentBrl` has up to 4 places; display only. */
export interface AiUsage {
  month: string;
  spentBrl: number;
  budgetBrl: number;
  calls: number;
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

  suggestCategories: (id: string) =>
    request<SuggestResult>(`/api/imports/${id}/suggest`, { method: 'POST' }),

  /** Answers the whole of `GET /api/auth/me`, so the caller can put it straight in the cache. */
  updateMe: (input: { aiEnabled: boolean }) =>
    request<AuthenticatedUser>('/api/auth/me', { method: 'PATCH', body: JSON.stringify(input) }),

  requestAnalysis: (month: string) =>
    request<{ analysisId: string }>('/api/ai/analyses', { method: 'POST', body: JSON.stringify({ month }) }),

  listAnalyses: (month: string) => request<AiAnalysis[]>(`/api/ai/analyses?${searchParams({ month })}`),

  aiUsage: () => request<AiUsage>('/api/ai/usage'),

  dashboardSummary: (month: string) =>
    request<DashboardSummary>(`/api/dashboard/summary?${searchParams({ month })}`),

  dashboardMonthly: (months: number) =>
    request<MonthTotals[]>(`/api/dashboard/monthly?${searchParams({ months })}`),

  dashboardByCategory: (month: string, kind: BreakdownKind) =>
    request<CategoryTotal[]>(`/api/dashboard/by-category?${searchParams({ month, kind })}`),

  searchMarketAssets: (q: string) => request<MarketAsset[]>(`/api/market-data/assets?${searchParams({ q })}`),

  registerMarketAsset: (input: MarketAssetInput) =>
    request<MarketAsset>('/api/market-data/assets', { method: 'POST', body: JSON.stringify(input) }),

  listSyncRuns: () => request<SyncRun[]>('/api/market-data/sync-runs'),

  triggerSync: () =>
    request<{ syncRunId: string }>('/api/market-data/sync', { method: 'POST', body: JSON.stringify({}) }),

  listPositions: () => request<Position[]>('/api/investments/assets'),

  portfolioSummary: () => request<PortfolioSummary>('/api/investments/summary'),

  addAsset: (input: AddAssetInput) =>
    request<Position>('/api/investments/assets', { method: 'POST', body: JSON.stringify(input) }),

  removeAsset: (id: string) => request<void>(`/api/investments/assets/${id}`, { method: 'DELETE' }),

  listMovements: (assetId: string) => request<Movement[]>(`/api/investments/assets/${assetId}/movements`),

  createMovement: (assetId: string, input: MovementInput) =>
    request<Movement>(`/api/investments/assets/${assetId}/movements`, { method: 'POST', body: JSON.stringify(input) }),

  updateMovement: (id: string, input: MovementInput) =>
    request<Movement>(`/api/investments/movements/${id}`, { method: 'PUT', body: JSON.stringify(input) }),

  deleteMovement: (id: string) => request<void>(`/api/investments/movements/${id}`, { method: 'DELETE' }),

  listDaily: (assetId: string) => request<DailyRow[]>(`/api/investments/assets/${assetId}/daily`),

  portfolioReturns: (query: ReturnsQuery) => request<Returns>(`/api/returns/portfolio?${searchParams(query)}`),

  assetReturns: (assetId: string, query: ReturnsQuery) =>
    request<AssetReturns>(`/api/returns/assets/${assetId}?${searchParams(query)}`),

  listCsvTemplates: () => request<CsvTemplate[]>('/api/csv-templates'),

  createCsvTemplate: (input: CsvTemplateInput) =>
    request<CsvTemplate>('/api/csv-templates', { method: 'POST', body: JSON.stringify(input) }),

  deleteCsvTemplate: (id: string) =>
    request<void>(`/api/csv-templates/${id}`, { method: 'DELETE' }),
};
