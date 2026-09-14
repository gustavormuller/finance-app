/**
 * The one dataset all six design previews render.
 *
 * Static on purpose: no database, no network, no clock. Screenshots of six variants
 * are only comparable if every one of them is drawing exactly the same numbers, and
 * a fixture that reaches for `new Date()` stops being reproducible the next morning.
 *
 * Throwaway. This whole folder is deleted at the start of checkpoint 5.
 */

export type Kind = 'Income' | 'Expense';

export interface PreviewTransaction {
  id: string;
  date: string;
  description: string;
  account: string;
  category: string;
  kind: Kind;
  /** Signed, as the API returns it: negative leaves, positive arrives. */
  amount: number;
}

export const accounts = ['Nubank', 'Inter', 'Cash'] as const;

export const categories = [
  'Salary',
  'Other income',
  'Housing',
  'Food',
  'Transport',
  'Leisure',
] as const;

/**
 * Twenty-five rows across three weeks, three accounts and six categories, from 4.50
 * to 12,847.30. The spread is the point: a variant that looks good on eight tidy
 * mid-range numbers and falls apart on a five-figure salary next to a bus fare has
 * not been tested.
 */
export const transactions: PreviewTransaction[] = [
  { id: 't25', date: '2026-09-14', description: 'Padaria Bela Vista', account: 'Cash', category: 'Food', kind: 'Expense', amount: -18.4 },
  { id: 't24', date: '2026-09-14', description: 'Uber to airport', account: 'Nubank', category: 'Transport', kind: 'Expense', amount: -73.9 },
  { id: 't23', date: '2026-09-13', description: 'Supermercado Pão de Açúcar', account: 'Nubank', category: 'Food', kind: 'Expense', amount: -412.67 },
  { id: 't22', date: '2026-09-13', description: 'Cinema ticket', account: 'Nubank', category: 'Leisure', kind: 'Expense', amount: -32.0 },
  { id: 't21', date: '2026-09-12', description: 'Freelance invoice 0043', account: 'Inter', category: 'Other income', kind: 'Income', amount: 4200.0 },
  { id: 't20', date: '2026-09-11', description: 'Metro top-up', account: 'Cash', category: 'Transport', kind: 'Expense', amount: -50.0 },
  { id: 't19', date: '2026-09-10', description: 'Electricity bill', account: 'Inter', category: 'Housing', kind: 'Expense', amount: -287.55 },
  { id: 't18', date: '2026-09-10', description: 'Coffee', account: 'Cash', category: 'Food', kind: 'Expense', amount: -4.5 },
  { id: 't17', date: '2026-09-09', description: 'Bookshop', account: 'Nubank', category: 'Leisure', kind: 'Expense', amount: -128.9 },
  { id: 't16', date: '2026-09-08', description: 'Rent', account: 'Inter', category: 'Housing', kind: 'Expense', amount: -2650.0 },
  { id: 't15', date: '2026-09-07', description: 'Petrol', account: 'Nubank', category: 'Transport', kind: 'Expense', amount: -213.44 },
  { id: 't14', date: '2026-09-05', description: 'Salary September', account: 'Nubank', category: 'Salary', kind: 'Income', amount: 12847.3 },
  { id: 't13', date: '2026-09-04', description: 'Feira da Vila Madalena', account: 'Cash', category: 'Food', kind: 'Expense', amount: -96.2 },
  { id: 't12', date: '2026-09-03', description: 'Streaming subscription', account: 'Nubank', category: 'Leisure', kind: 'Expense', amount: -55.9 },
  { id: 't11', date: '2026-09-02', description: 'Bus fare', account: 'Cash', category: 'Transport', kind: 'Expense', amount: -6.4 },
  { id: 't10', date: '2026-09-01', description: 'Water bill', account: 'Inter', category: 'Housing', kind: 'Expense', amount: -143.18 },
  { id: 't09', date: '2026-08-31', description: 'Sold old monitor', account: 'Inter', category: 'Other income', kind: 'Income', amount: 680.0 },
  { id: 't08', date: '2026-08-30', description: 'Restaurant with Marina', account: 'Nubank', category: 'Food', kind: 'Expense', amount: -324.75 },
  { id: 't07', date: '2026-08-29', description: 'Pharmacy', account: 'Nubank', category: 'Food', kind: 'Expense', amount: -87.3 },
  { id: 't06', date: '2026-08-28', description: 'Internet', account: 'Inter', category: 'Housing', kind: 'Expense', amount: -119.9 },
  { id: 't05', date: '2026-08-28', description: 'Taxi home', account: 'Cash', category: 'Transport', kind: 'Expense', amount: -41.0 },
  { id: 't04', date: '2026-08-27', description: 'Concert tickets', account: 'Nubank', category: 'Leisure', kind: 'Expense', amount: -460.0 },
  { id: 't03', date: '2026-08-26', description: 'Bakery', account: 'Cash', category: 'Food', kind: 'Expense', amount: -22.85 },
  { id: 't02', date: '2026-08-25', description: 'Building maintenance fee', account: 'Inter', category: 'Housing', kind: 'Expense', amount: -845.0 },
  { id: 't01', date: '2026-08-25', description: 'Refund — cancelled flight', account: 'Nubank', category: 'Other income', kind: 'Income', amount: 1189.6 },
];

/** Formatted the way the real screens will: BRL, two decimals, explicit sign. */
export function formatAmount(amount: number): string {
  const formatted = Math.abs(amount).toLocaleString('pt-BR', {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  });

  return `${amount < 0 ? '−' : '+'}${formatted}`;
}

/** Without a sign, for layouts that carry direction some other way. */
export function formatBare(amount: number): string {
  return Math.abs(amount).toLocaleString('pt-BR', {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  });
}

export function formatDay(date: string): string {
  const [, month, day] = date.split('-');

  return `${day}/${month}`;
}

export function formatLongDay(date: string): string {
  const months = [
    'January', 'February', 'March', 'April', 'May', 'June',
    'July', 'August', 'September', 'October', 'November', 'December',
  ];
  const [year, month, day] = date.split('-');

  return `${months[Number(month) - 1]} ${Number(day)}, ${year}`;
}

/** Rows in the order every variant shows them: newest first. */
export const byDateDescending = [...transactions].sort((a, b) => b.date.localeCompare(a.date));

export const largestAmount = Math.max(...transactions.map((row) => Math.abs(row.amount)));

/** For the grouped variant: days, each with its rows and its net. */
export function groupByDay(rows: PreviewTransaction[]) {
  const days = new Map<string, PreviewTransaction[]>();

  for (const row of rows) {
    days.set(row.date, [...(days.get(row.date) ?? []), row]);
  }

  return [...days.entries()].map(([date, items]) => ({
    date,
    items,
    subtotal: items.reduce((total, item) => total + item.amount, 0),
  }));
}
