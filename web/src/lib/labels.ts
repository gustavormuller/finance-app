import type { AccountType, CategoryKind } from '@/api/finance';

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
};

/** Plural, for the headings that group a whole list by kind. */
export const categoryKindPlurals: Record<CategoryKind, string> = {
  Income: 'Receitas',
  Expense: 'Despesas',
};

export const accountTypes: AccountType[] = [
  'Checking',
  'Savings',
  'CreditCard',
  'Cash',
  'Investment',
];

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
