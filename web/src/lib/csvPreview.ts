import type { AmountCulture, SignMode } from '@/api/finance';

/**
 * The mapping step's live preview: the first rows of the file "as they would be
 * interpreted", so that `03/04` visibly becomes 3 April under one format and 4 March
 * under the other before anything is uploaded.
 *
 * A preview, not the import. The API is authoritative and runs the same rules over
 * the whole file; this mirrors them for five rows so the choice is visible. Two
 * things are deliberate:
 *
 * - Amounts never become a JavaScript number. They are parsed as text into a
 *   canonical decimal string and formatted from that string, so no float sits in
 *   the path even here (ADR-002).
 * - Dates are matched against the declared format token by token. Nothing is
 *   inferred, and a date that does not fit is an issue, never the other order.
 */

export interface MappingDraft {
  hasHeader: boolean;
  culture: AmountCulture;
  dateFormat: string;
  signMode: SignMode;
  dateColumn: string;
  amountColumn: string;
  debitColumn: string;
  creditColumn: string;
  /** Column references in the order they were chosen. */
  descriptionColumns: string[];
}

export interface InterpretedRow {
  /** ISO `yyyy-MM-dd`, or null with an issue. */
  date: string | null;
  /** Canonical decimal text such as `-1234.56`, or null with an issue. */
  amount: string | null;
  description: string;
  issues: string[];
}

const MONTHS = ['jan', 'fev', 'mar', 'abr', 'mai', 'jun', 'jul', 'ago', 'set', 'out', 'nov', 'dez'];

const SEPARATORS: Record<AmountCulture, { decimal: string; group: string; symbol: string }> = {
  'pt-BR': { decimal: ',', group: '.', symbol: 'R$' },
  'en-US': { decimal: '.', group: ',', symbol: '$' },
};

/**
 * A header name first, exact then case-insensitive, then a 0-based index — the
 * same order the API resolves references in.
 */
export function resolveColumn(reference: string, headers: string[] | null, width: number): number | null {
  const wanted = reference.trim();

  if (wanted === '') {
    return null;
  }

  if (headers) {
    const exact = headers.findIndex((header) => header.trim() === wanted);

    if (exact >= 0) {
      return exact;
    }

    const loose = headers.findIndex((header) => header.trim().toLowerCase() === wanted.toLowerCase());

    if (loose >= 0) {
      return loose;
    }
  }

  const index = /^\d+$/.test(wanted) ? Number(wanted) : -1;

  return index >= 0 && index < width ? index : null;
}

function daysIn(year: number, month: number): number {
  return new Date(Date.UTC(year, month, 0)).getUTCDate();
}

/**
 * Exact match of `text` against a .NET-style `format` (`d`, `dd`, `M`, `MM`, `yy`,
 * `yyyy`, `HH`, `mm`, `ss`, and literal separators). Time tokens are consumed and
 * discarded: the file's date is a calendar day.
 */
export function parseExactDate(text: string, format: string): string | null {
  const input = text.trim();
  let position = 0;
  let day: number | null = null;
  let month: number | null = null;
  let year: number | null = null;

  const tokens = format.match(/d+|M+|y+|H+|m+|s+|./g) ?? [];

  for (const token of tokens) {
    const kind = token[0];

    if (kind && 'dMyHms'.includes(kind)) {
      const exact = token.length >= 2;
      const width = kind === 'y' ? token.length : exact ? 2 : 2;
      const digits = /^\d+/.exec(input.slice(position))?.[0] ?? '';
      const taken = exact ? digits.slice(0, width) : digits.slice(0, 2);

      if (taken.length === 0 || (exact && taken.length !== width)) {
        return null;
      }

      position += taken.length;
      const value = Number(taken);

      if (kind === 'd') day = value;
      if (kind === 'M') month = value;
      if (kind === 'y') year = token.length <= 2 ? 2000 + value : value;
    } else if (input[position] === token) {
      position += 1;
    } else {
      return null;
    }
  }

  if (position !== input.length || day === null || month === null || year === null) {
    return null;
  }

  if (month < 1 || month > 12 || day < 1 || day > daysIn(year, month)) {
    return null;
  }

  return `${String(year).padStart(4, '0')}-${String(month).padStart(2, '0')}-${String(day).padStart(2, '0')}`;
}

/**
 * Text to canonical decimal text under the declared separators. Accepts a currency
 * symbol, surrounding whitespace, a leading or trailing sign and accounting
 * parentheses; rejects anything else, including the other culture's separators.
 */
export function parseAmountText(text: string, culture: AmountCulture): string | null {
  const { decimal, group, symbol } = SEPARATORS[culture];
  let body = text.trim().split(symbol).join('').trim();
  let negative = false;

  if (body.startsWith('(') && body.endsWith(')')) {
    negative = true;
    body = body.slice(1, -1).trim();
  }

  if (body.startsWith('-') || body.startsWith('+')) {
    negative = body.startsWith('-') || negative;
    body = body.slice(1).trim();
  } else if (body.endsWith('-') || body.endsWith('+')) {
    negative = body.endsWith('-') || negative;
    body = body.slice(0, -1).trim();
  }

  if (body === '' || /[+\-()]/.test(body)) {
    return null;
  }

  const parts = body.split(decimal);

  if (parts.length > 2) {
    return null;
  }

  const integer = (parts[0] ?? '').split(group).join('');
  const fraction = parts[1] ?? '';

  if (!/^\d+$/.test(integer) || !/^\d*$/.test(fraction)) {
    return null;
  }

  const magnitude = fraction.length > 0 ? `${integer}.${fraction}` : integer;
  const isZero = /^[0.]*$/.test(magnitude);

  return `${negative && !isZero ? '-' : ''}${magnitude}`;
}

function negate(amount: string): string {
  return amount.startsWith('-') ? amount.slice(1) : /^[0.]*$/.test(amount) ? amount : `-${amount}`;
}

function absolute(amount: string): string {
  return amount.startsWith('-') ? amount.slice(1) : amount;
}

export function interpretRows(
  headers: string[] | null,
  rows: string[][],
  mapping: MappingDraft,
): InterpretedRow[] {
  const width = headers?.length ?? rows[0]?.length ?? 0;
  const dateColumn = resolveColumn(mapping.dateColumn, headers, width);
  const amountColumn = resolveColumn(mapping.amountColumn, headers, width);
  const debitColumn = resolveColumn(mapping.debitColumn, headers, width);
  const creditColumn = resolveColumn(mapping.creditColumn, headers, width);
  const descriptionColumns = mapping.descriptionColumns
    .map((reference) => resolveColumn(reference, headers, width))
    .filter((index): index is number => index !== null);

  return rows.map((row) => {
    const issues: string[] = [];
    const cell = (index: number | null) => (index === null ? '' : (row[index] ?? '')).trim();

    const dateText = cell(dateColumn);
    const date = dateColumn === null ? null : parseExactDate(dateText, mapping.dateFormat);

    if (date === null) {
      issues.push(dateColumn === null ? 'Escolha a coluna de data' : `Data inválida: "${dateText}"`);
    }

    const parse = (index: number | null): string | null | undefined => {
      const text = cell(index);

      if (index === null || text === '') {
        return undefined;
      }

      const parsed = parseAmountText(text, mapping.culture);

      if (parsed === null) {
        issues.push(`Valor inválido: "${text}"`);
      }

      return parsed;
    };

    let amount: string | null = null;

    if (mapping.signMode === 'DebitCredit') {
      const debit = parse(debitColumn);
      const credit = parse(creditColumn);
      const hasDebit = !!debit && !/^[0.]*$/.test(debit);
      const hasCredit = !!credit && !/^[0.]*$/.test(credit);

      if (hasDebit && hasCredit) {
        issues.push('Linha tem débito e crédito');
      } else if (hasDebit) {
        amount = `-${absolute(debit)}`;
      } else if (hasCredit) {
        amount = absolute(credit);
      } else if (debit === undefined && credit === undefined) {
        issues.push('Valor ausente');
      }
    } else {
      const signed = parse(amountColumn);

      if (signed === undefined) {
        issues.push(amountColumn === null ? 'Escolha a coluna de valor' : 'Valor ausente');
      } else if (signed !== null) {
        amount = mapping.signMode === 'SignedInverted' ? negate(signed) : signed;
      }
    }

    const description = descriptionColumns
      .map((index) => cell(index))
      .filter((part) => part !== '')
      .join(' — ');

    return { date, amount, description, issues };
  });
}

/** `2026-04-03` as `3 abr 2026`, so a swapped day and month is visible at a glance. */
export function formatPreviewDate(isoDay: string): string {
  const [year, month, day] = isoDay.split('-').map(Number);

  return `${day} ${MONTHS[(month ?? 1) - 1]} ${year}`;
}

/** Canonical `-1234.5` as `−1.234,50`, grouped and signed from the text alone. */
export function formatPreviewAmount(canonical: string): string {
  const negative = canonical.startsWith('-');
  const [integer = '0', fraction = ''] = absolute(canonical).split('.');
  const grouped = integer.replace(/\B(?=(\d{3})+(?!\d))/g, '.');
  const cents = (fraction + '00').slice(0, Math.max(2, fraction.length));

  return `${negative ? '−' : '+'}${grouped},${cents}`;
}
