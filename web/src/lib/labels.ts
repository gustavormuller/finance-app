import type {
  AccountType,
  AmountCulture,
  AnalysisStatus,
  CategoryKind,
  CategorySource,
  CompareCurrency,
  ComparePeriodKind,
  ImportBatchStatus,
  ImportSource,
  MarketAssetClass,
  MovementKind,
  ProviderKind,
  ReturnsPeriodKind,
  SignMode,
  StagedRowStatus,
  SyncRunStatus,
  SyncTrigger,
} from '@/api/finance';

/**
 * How the API's enum members are written on screen.
 *
 * The members themselves stay English — they are identifiers, they are the wire
 * contract, and they are the `int` in the column. Only the reading of them is
 * Portuguese, which is why the mapping lives here and not in the enum.
 */
export const accountTypeLabels: Record<AccountType, string> = {
  Checking: 'Conta corrente',
  Savings: 'Poupança',
  CreditCard: 'Cartão de crédito',
  Cash: 'Dinheiro',
  Investment: 'Investimento',
};

export const categoryKindLabels: Record<CategoryKind, string> = {
  Income: 'Receita',
  Expense: 'Despesa',
  Transfer: 'Transferência',
};

/** Plural, for the headings that group a whole list by kind. */
export const categoryKindPlurals: Record<CategoryKind, string> = {
  Income: 'Receitas',
  Expense: 'Despesas',
  Transfer: 'Transferências',
};

/** The order kinds are offered and grouped in, everywhere a list of them appears. */
export const categoryKinds: CategoryKind[] = ['Income', 'Expense', 'Transfer'];

export const importSourceLabels: Record<ImportSource, string> = {
  Ofx: 'OFX',
  Csv: 'CSV',
  Spreadsheet: 'Planilha',
};

export const importBatchStatusLabels: Record<ImportBatchStatus, string> = {
  Staged: 'Em revisão',
  Committed: 'Confirmada',
};

export const stagedRowStatusLabels: Record<StagedRowStatus, string> = {
  Ready: 'Pronta',
  Duplicate: 'Duplicada',
  Invalid: 'Inválida',
};

/** Which rung chose a staged row's category. The preview only marks `Ai`. */
export const categorySourceLabels: Record<CategorySource, string> = {
  None: 'Sem categoria',
  History: 'Pelo histórico',
  Default: 'Categoria padrão',
  Ai: 'Sugerida pela IA',
  User: 'Escolhida por você',
};

export const analysisStatusLabels: Record<AnalysisStatus, string> = {
  Pending: 'Na fila',
  Running: 'Gerando',
  Completed: 'Concluída',
  Failed: 'Falhou',
};

export const signModeLabels: Record<SignMode, string> = {
  Signed: 'Valor com sinal (negativo sai)',
  SignedInverted: 'Valor com sinal invertido (positivo sai, como em faturas)',
  DebitCredit: 'Colunas separadas de débito e crédito',
};

export const amountCultureLabels: Record<AmountCulture, string> = {
  'pt-BR': 'Brasileiro (1.234,56)',
  'en-US': 'Americano (1,234.56)',
};

export const accountTypes: AccountType[] = [
  'Checking',
  'Savings',
  'CreditCard',
  'Cash',
  'Investment',
];

export const marketAssetClassLabels: Record<MarketAssetClass, string> = {
  StockBr: 'Ação (B3)',
  Fii: 'Fundo imobiliário',
  EtfBr: 'ETF (B3)',
  Bdr: 'BDR',
  StockUs: 'Ação (EUA)',
  Crypto: 'Criptomoeda',
  Index: 'Índice',
  Currency: 'Câmbio',
};

export const marketAssetClasses: MarketAssetClass[] = ['StockBr', 'Fii', 'EtfBr', 'Bdr', 'StockUs', 'Crypto', 'Index', 'Currency'];

/** What a portfolio can hold: indices and exchange rates are only compared against (025). */
export const holdableAssetClasses: MarketAssetClass[] = marketAssetClasses.filter(
  (value) => value !== 'Index' && value !== 'Currency',
);

/** Provider names are brands, written as each writes itself. */
export const providerKindLabels: Record<ProviderKind, string> = {
  Yahoo: 'Yahoo Finance',
  Brapi: 'brapi',
  CoinGecko: 'CoinGecko',
  TwelveData: 'Twelve Data',
  Binance: 'Binance',
};

/** What each provider prices, beside its name where one is picked; the catalogue shows the plain name. */
export const providerKindCoverage: Record<ProviderKind, string> = {
  Yahoo: 'B3, EUA, índices, cripto em US$, câmbio — sem chave',
  Brapi: 'B3: ações, FIIs, ETFs',
  CoinGecko: 'cripto',
  TwelveData: 'ações dos EUA',
  Binance: 'cripto em reais',
};

/** In the order a select offers them: Yahoo first, the keyless source of nearly everything (025). */
export const providerKinds: ProviderKind[] = ['Yahoo', 'Brapi', 'CoinGecko', 'TwelveData', 'Binance'];

const syncSectionLabels: Record<string, string> = {
  Bcb: 'Banco Central (SGS)',
  Snapshots: 'Posições da carteira',
};

/**
 * A key of a sync run's summary: a `ProviderKind` member, `Bcb` for the benchmark series,
 * or `Snapshots` for the snapshot rebuild after the sync. An unknown key is shown as sent rather
 * than hidden.
 */
export function syncProviderLabel(key: string): string {
  return syncSectionLabels[key] ?? providerKindLabels[key as ProviderKind] ?? key;
}

export const syncTriggerLabels: Record<SyncTrigger, string> = {
  Scheduled: 'Agendada',
  Manual: 'Manual',
};

export const syncRunStatusLabels: Record<SyncRunStatus, string> = {
  Running: 'Em andamento',
  Succeeded: 'Concluída',
  PartialFailure: 'Concluída com falhas',
  Failed: 'Falhou',
};

const healthWords: Record<string, string> = {
  ok: 'ok',
  degraded: 'com falha',
  unreachable: 'inacessível',
};

/** A word of `/api/health` (001): `ok`, `degraded` or `unreachable`. An unknown word is shown as sent. */
export function healthLabel(word: string): string {
  return healthWords[word] ?? word;
}

export const movementKindLabels: Record<MovementKind, string> = {
  Buy: 'Compra',
  Sell: 'Venda',
  Dividend: 'Dividendo',
  Jcp: 'JCP',
  Split: 'Desdobramento',
};

export const movementKinds: MovementKind[] = ['Buy', 'Sell', 'Dividend', 'Jcp', 'Split'];

/** Keyed by the code the returns API sends. The names are spec 008's configuration labels. */
const benchmarkLabels: Record<string, string> = {
  CDI: 'CDI',
  SELIC: 'SELIC',
  IPCA6: 'IPCA + 6%',
  USDBRL: 'Dólar',
  IVVB11: 'S&P 500 (IVVB11)',
};

/** The order benchmarks are listed and drawn in: the spec's configuration order. */
export const benchmarkCodes = ['CDI', 'SELIC', 'IPCA6', 'USDBRL', 'IVVB11'];

/** A benchmark's name on screen; an unknown code is shown as sent rather than hidden. */
export function benchmarkLabel(code: string): string {
  return benchmarkLabels[code] ?? code;
}

/** Where the full name does not fit, as in the comparison tiles. */
const benchmarkShortLabels: Record<string, string> = {
  IVVB11: 'S&P 500',
};

export function benchmarkShortLabel(code: string): string {
  return benchmarkShortLabels[code] ?? benchmarkLabel(code);
}

export const returnsPeriodLabels: Record<ReturnsPeriodKind, string> = {
  inception: 'Desde o início',
  ytd: 'No ano',
  '12m': '12 meses',
  custom: 'Personalizado',
};

export const returnsPeriods: ReturnsPeriodKind[] = ['inception', 'ytd', '12m', 'custom'];

/** 026's periods, short as a finance page writes them; `comparePeriodTitles` spells each out. */
export const comparePeriodLabels: Record<ComparePeriodKind, string> = {
  '1m': '1M',
  '6m': '6M',
  ytd: 'YTD',
  '1y': '1A',
  '5y': '5A',
  '10y': '10A',
  max: 'Máx',
  custom: 'Personalizado',
};

export const comparePeriodTitles: Record<ComparePeriodKind, string> = {
  '1m': '1 mês',
  '6m': '6 meses',
  ytd: 'No ano',
  '1y': '1 ano',
  '5y': '5 anos',
  '10y': '10 anos',
  max: 'Desde o começo das séries',
  custom: 'De uma data a outra',
};

export const comparePeriods: ComparePeriodKind[] = ['1m', '6m', 'ytd', '1y', '5y', '10y', 'max', 'custom'];

export const compareCurrencyLabels: Record<CompareCurrency, string> = {
  original: 'Moeda original',
  BRL: 'Converter para R$',
  USD: 'Converter para US$',
};

export const compareCurrencies: CompareCurrency[] = ['original', 'BRL', 'USD'];

/**
 * The stored benchmark series a comparison offers (026), keyed by the code the API takes, in
 * the order they are listed. An unknown code is shown as sent.
 */
export const compareBenchmarks: { code: string; name: string; description: string }[] = [
  { code: 'CDI', name: 'CDI', description: 'Taxa DI, acumulada' },
  { code: 'SELIC', name: 'SELIC', description: 'Taxa Selic, acumulada' },
  { code: 'IPCA', name: 'IPCA', description: 'Inflação oficial, acumulada' },
  { code: 'USDBRL', name: 'Dólar (PTAX)', description: 'Reais por dólar, do Banco Central' },
  { code: 'IVVB11', name: 'S&P 500 (IVVB11)', description: 'ETF do S&P 500 negociado em reais' },
];

/** How a currency code reads beside a figure: `R$`, `US$`, or the code itself. */
export function currencySymbol(code: string): string {
  return code === 'BRL' ? 'R$' : code === 'USD' ? 'US$' : code;
}

/**
 * `2026-09-13` as `13/09/2026`.
 *
 * Split rather than passed through `Date`: `new Date('2026-09-13')` parses as
 * midnight UTC and renders as the 12th anywhere west of Greenwich, which is exactly
 * the timezone bug the `date` column was chosen to avoid. There is no instant here to
 * convert, so nothing converts it.
 */
export function formatDate(isoDay: string): string {
  const [year, month, day] = isoDay.split('-');

  return year && month && day ? `${day}/${month}/${year}` : isoDay;
}
