import { describe, expect, it } from 'vitest';

import {
  formatPreviewAmount,
  formatPreviewDate,
  interpretRows,
  parseAmountText,
  parseExactDate,
  type MappingDraft,
} from './csvPreview';

const mapping: MappingDraft = {
  hasHeader: true,
  culture: 'pt-BR',
  dateFormat: 'dd/MM/yyyy',
  signMode: 'Signed',
  dateColumn: 'Data',
  amountColumn: 'Valor',
  debitColumn: '',
  creditColumn: '',
  descriptionColumns: ['Histórico', 'Descrição'],
};

const headers = ['Data', 'Histórico', 'Descrição', 'Valor'];

describe('parseExactDate', () => {
  it('reads 03/04 as the third of April under dd/MM/yyyy and the fourth of March under MM/dd/yyyy', () => {
    expect(parseExactDate('03/04/2026', 'dd/MM/yyyy')).toBe('2026-04-03');
    expect(parseExactDate('03/04/2026', 'MM/dd/yyyy')).toBe('2026-03-04');
  });

  it('fails loudly on a date that does not fit the format instead of rolling over', () => {
    expect(parseExactDate('31/12/2026', 'MM/dd/yyyy')).toBeNull();
    expect(parseExactDate('30/02/2026', 'dd/MM/yyyy')).toBeNull();
    expect(parseExactDate('3/4/2026', 'dd/MM/yyyy')).toBeNull();
    expect(parseExactDate('2026-04-03', 'dd/MM/yyyy')).toBeNull();
    expect(parseExactDate('03/04/2026 10:22', 'dd/MM/yyyy')).toBeNull();
    expect(parseExactDate('', 'dd/MM/yyyy')).toBeNull();
  });

  it('accepts the formats exports actually use', () => {
    expect(parseExactDate('2026-04-03', 'yyyy-MM-dd')).toBe('2026-04-03');
    expect(parseExactDate('3/4/2026', 'd/M/yyyy')).toBe('2026-04-03');
    expect(parseExactDate('03/04/26', 'dd/MM/yy')).toBe('2026-04-03');
    expect(parseExactDate('03/04/2026 10:22', 'dd/MM/yyyy HH:mm')).toBe('2026-04-03');
    expect(parseExactDate('29/02/2028', 'dd/MM/yyyy')).toBe('2028-02-29');
  });
});

describe('parseAmountText', () => {
  it('reads Brazilian and American separators as text, never as a float', () => {
    expect(parseAmountText('1.234,56', 'pt-BR')).toBe('1234.56');
    expect(parseAmountText('-1.234.567,89', 'pt-BR')).toBe('-1234567.89');
    expect(parseAmountText('R$ 1.234,56', 'pt-BR')).toBe('1234.56');
    expect(parseAmountText('-R$ 50,00', 'pt-BR')).toBe('-50.00');
    expect(parseAmountText('(50,00)', 'pt-BR')).toBe('-50.00');
    expect(parseAmountText('50,00-', 'pt-BR')).toBe('-50.00');
    expect(parseAmountText('1,234.56', 'en-US')).toBe('1234.56');
    expect(parseAmountText('-58.00', 'en-US')).toBe('-58.00');
    expect(parseAmountText('0,001', 'pt-BR')).toBe('0.001');
  });

  it('refuses the other culture and garbage', () => {
    expect(parseAmountText('1.234,56', 'en-US')).toBeNull();
    expect(parseAmountText('1,234.56', 'pt-BR')).toBeNull();
    expect(parseAmountText('abc', 'pt-BR')).toBeNull();
    expect(parseAmountText('', 'pt-BR')).toBeNull();
    expect(parseAmountText('--5,00', 'pt-BR')).toBeNull();
  });
});

describe('interpretRows', () => {
  it('re-reads the same rows differently when the date format changes', () => {
    const rows = [['03/04/2026', 'Pix enviado', 'JOAO', '-1.250,00']];

    const dayFirst = interpretRows(headers, rows, mapping);
    const monthFirst = interpretRows(headers, rows, { ...mapping, dateFormat: 'MM/dd/yyyy' });

    expect(dayFirst[0]).toEqual({
      date: '2026-04-03',
      amount: '-1250.00',
      description: 'Pix enviado — JOAO',
      issues: [],
    });
    expect(monthFirst[0]?.date).toBe('2026-03-04');
  });

  it('inverts the sign for card statements and maps debit and credit columns', () => {
    const inverted = interpretRows(headers, [['03/04/2026', 'Uber', '', '58,00']], {
      ...mapping,
      signMode: 'SignedInverted',
    });
    expect(inverted[0]?.amount).toBe('-58.00');

    const sheet = ['Data', 'Lançamento', 'Crédito', 'Débito'];
    const debitCredit = interpretRows(
      sheet,
      [
        ['01/09/2026', 'Salário', '3.000,00', ''],
        ['02/09/2026', 'Luz', '', '150,00'],
        ['03/09/2026', 'Erro', '10,00', '10,00'],
      ],
      {
        ...mapping,
        signMode: 'DebitCredit',
        amountColumn: '',
        debitColumn: 'Débito',
        creditColumn: 'Crédito',
        descriptionColumns: ['Lançamento'],
      },
    );

    expect(debitCredit.map((row) => row.amount)).toEqual(['3000.00', '-150.00', null]);
    expect(debitCredit[2]?.issues).toEqual(['Linha tem débito e crédito']);
  });

  it('reports every problem on a row and resolves columns by index without a header', () => {
    const bad = interpretRows(headers, [['ontem', 'A', 'B', 'muito']], mapping);
    expect(bad[0]?.issues).toEqual(['Data inválida: "ontem"', 'Valor inválido: "muito"']);

    const byIndex = interpretRows(null, [['10/09/2026', 'Loja', '-1,00']], {
      ...mapping,
      dateColumn: '0',
      amountColumn: '2',
      descriptionColumns: ['1'],
    });
    expect(byIndex[0]).toMatchObject({ date: '2026-09-10', amount: '-1.00', description: 'Loja' });
  });
});

describe('formatting', () => {
  it('shows the month by name so a swapped day and month is visible', () => {
    expect(formatPreviewDate('2026-04-03')).toBe('3 abr 2026');
    expect(formatPreviewDate('2026-03-04')).toBe('4 mar 2026');
  });

  it('groups and signs from the canonical text', () => {
    expect(formatPreviewAmount('-1234.5')).toBe('−1.234,50');
    expect(formatPreviewAmount('3000')).toBe('+3.000,00');
    expect(formatPreviewAmount('0.001')).toBe('+0,001');
  });
});
