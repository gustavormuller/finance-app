import { ApiError, type ImportBatch } from '@/api/finance';

/** The account page's tabs; `transactions` is the default and is left out of the URL. */
export type AccountTab = 'transactions' | 'import' | 'details';

export const accountTabs: AccountTab[] = ['transactions', 'import', 'details'];

/** The fields the account form renders, so a 400 naming one is shown under it. */
const FIELDS = ['name', 'type', 'currency', 'openingBalance'];

/**
 * An opening balance as typed. pt-BR first — `-1.234,56` — because that is how the
 * app shows money; a plain `1234.56` still works when there is no comma. The sign is
 * kept (a credit card starts negative), including the U+2212 minus `Amount` prints.
 * Empty means zero. NaN means "not a number", for the caller to refuse.
 */
export function parseOpeningBalance(typed: string): number {
  const text = typed.trim().replace('−', '-');

  if (text === '') {
    return 0;
  }

  return Number(text.includes(',') ? text.replaceAll('.', '').replace(',', '.') : text);
}

/** Shown back the way it is typed: decimal comma, no grouping. */
export function typedOpeningBalance(value: number): string {
  return value.toFixed(2).replace('.', ',');
}

export interface Refusal {
  /** Messages for the fields the form renders, shown under each. */
  fields: Record<string, string[]>;
  /** The sentence shown above the form, never the English default title of a 400. */
  message: string | null;
}

/** A refused account write: a 400's messages go under their fields, anything else is one sentence. */
export function accountRefusal(error: Error): Refusal {
  const fields = error instanceof ApiError ? error.fields : {};
  const elsewhere = Object.entries(fields)
    .filter(([field]) => !FIELDS.includes(field))
    .flatMap(([, messages]) => messages);

  return {
    fields,
    message: Object.keys(fields).length === 0 ? error.message : elsewhere.length > 0 ? elsewhere.join(' ') : null,
  };
}

/**
 * The line under an account's name (015, decision 4): the batch in review, else the
 * day of the latest commit — without the year inside the current one — else none.
 */
export function lastImportLine(batches: ImportBatch[], accountId: string, now = new Date()): string {
  const own = batches.filter((batch) => batch.accountId === accountId);

  if (own.some((batch) => batch.status === 'Staged')) {
    return 'Extrato em revisão';
  }

  const latest = own
    .map((batch) => (batch.committedAt ? new Date(batch.committedAt) : null))
    .filter((day): day is Date => day !== null)
    .reduce<Date | null>((newest, day) => (newest && newest >= day ? newest : day), null);

  if (!latest) {
    return 'Sem importações';
  }

  const day = latest.toLocaleDateString(
    'pt-BR',
    latest.getFullYear() === now.getFullYear()
      ? { day: '2-digit', month: '2-digit' }
      : { day: '2-digit', month: '2-digit', year: 'numeric' },
  );

  return `Último extrato em ${day}`;
}
