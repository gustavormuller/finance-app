import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import type { Category, ImportBatchDetail, StagedRow } from '@/api/finance';

import PreviewStep from './PreviewStep';

const categories: Category[] = [
  { id: 'cat-food', name: 'Alimentação', kind: 'Expense', parentId: null, createdAt: '' },
  { id: 'cat-other', name: 'Outros', kind: 'Expense', parentId: null, createdAt: '' },
  { id: 'cat-income', name: 'Outras receitas', kind: 'Income', parentId: null, createdAt: '' },
  { id: 'cat-transfer', name: 'Transferência', kind: 'Transfer', parentId: null, createdAt: '' },
];

function row(partial: Partial<StagedRow> & Pick<StagedRow, 'id' | 'rowNumber' | 'status'>): StagedRow {
  return {
    date: '2026-09-10',
    amount: -42.9,
    currency: 'BRL',
    rawDescription: 'PAG*IFOOD 10/09',
    externalId: null,
    categoryId: 'cat-other',
    included: partial.status === 'Ready',
    issues: [],
    ...partial,
  };
}

function detail(rows: StagedRow[]): ImportBatchDetail {
  return {
    batch: {
      id: 'batch-1',
      accountId: 'acc-1',
      accountName: 'Nubank',
      source: 'Ofx',
      fileName: 'extrato.ofx',
      status: 'Staged',
      rowCount: rows.length,
      committedCount: null,
      createdAt: '2026-09-18T00:00:00Z',
      committedAt: null,
    },
    counts: {
      ready: rows.filter((item) => item.status === 'Ready').length,
      duplicates: rows.filter((item) => item.status === 'Duplicate').length,
      invalid: rows.filter((item) => item.status === 'Invalid').length,
      included: rows.filter((item) => item.included).length,
    },
    rows: { items: rows, page: 1, pageSize: 50, total: rows.length },
  };
}

function renderStep(rows: StagedRow[]) {
  const onPatchRow = vi.fn();
  const onCommit = vi.fn();

  render(
    <PreviewStep
      detail={detail(rows)}
      categories={categories}
      filter=""
      busy={false}
      error={null}
      onFilter={vi.fn()}
      onPage={vi.fn()}
      onPatchRow={onPatchRow}
      onCommit={onCommit}
      onDiscard={vi.fn()}
    />,
  );

  return { onPatchRow, onCommit };
}

describe('PreviewStep', () => {
  /** Spec web unit test 59. */
  it('disables commit while nothing would be written', () => {
    renderStep([
      row({ id: 'r1', rowNumber: 1, status: 'Duplicate' }),
      row({ id: 'r2', rowNumber: 2, status: 'Invalid', issues: ['Valor não pode ser zero'] }),
    ]);

    expect(screen.getByRole('button', { name: 'Confirmar importação' })).toBeDisabled();
    expect(screen.getByTestId('preview-counts')).toHaveTextContent('0 a importar');
  });

  it('enables commit once a row is included', () => {
    const { onCommit } = renderStep([row({ id: 'r1', rowNumber: 1, status: 'Ready' })]);

    const commit = screen.getByRole('button', { name: 'Confirmar importação' });
    expect(commit).toBeEnabled();

    commit.click();
    expect(onCommit).toHaveBeenCalledTimes(1);
  });

  /** Spec web unit test 60. An invalid row has no way in, and shows why. */
  it('gives an invalid row no include checkbox and shows its issues', () => {
    renderStep([
      row({ id: 'r1', rowNumber: 7, status: 'Invalid', amount: null, issues: ['Data inválida: "ontem"'] }),
    ]);

    expect(screen.queryByRole('checkbox', { name: 'Incluir linha 7' })).not.toBeInTheDocument();
    expect(screen.getByText('Data inválida: "ontem"')).toBeInTheDocument();
  });

  /** Spec web unit test 61. */
  it('excludes duplicates by default and includes one when its box is ticked', async () => {
    const user = userEvent.setup();
    const { onPatchRow } = renderStep([
      row({ id: 'r1', rowNumber: 1, status: 'Ready' }),
      row({ id: 'r2', rowNumber: 2, status: 'Duplicate' }),
    ]);

    expect(screen.getByRole('checkbox', { name: 'Incluir linha 1' })).toBeChecked();

    const duplicate = screen.getByRole('checkbox', { name: 'Incluir linha 2' });
    expect(duplicate).not.toBeChecked();

    await user.click(duplicate);

    expect(onPatchRow).toHaveBeenCalledWith(expect.objectContaining({ id: 'r2' }), { include: true });
  });

  it('offers only categories of the kind that agrees with the sign', () => {
    renderStep([
      row({ id: 'r1', rowNumber: 1, status: 'Ready', amount: -10 }),
      row({ id: 'r2', rowNumber: 2, status: 'Ready', amount: 10, categoryId: 'cat-income' }),
    ]);

    const expense = screen.getByRole('combobox', { name: 'Categoria da linha 1' });
    const income = screen.getByRole('combobox', { name: 'Categoria da linha 2' });

    expect(Array.from(expense.querySelectorAll('option')).map((option) => option.textContent)).toEqual([
      'Alimentação',
      'Outros',
    ]);
    expect(Array.from(income.querySelectorAll('option')).map((option) => option.textContent)).toEqual([
      'Outras receitas',
    ]);
  });

  /**
   * 005 amendment 1. A transfer takes any sign, so Transferência is offered next to
   * the kind the sign implies, on a leaving row and an arriving one alike.
   */
  it('offers a Transfer category whatever the row sign', () => {
    renderStep([
      row({ id: 'r1', rowNumber: 1, status: 'Ready', amount: -3000 }),
      row({ id: 'r2', rowNumber: 2, status: 'Ready', amount: 3000, categoryId: 'cat-income' }),
    ]);

    const options = (rowNumber: number) =>
      [...screen.getByLabelText(`Categoria da linha ${rowNumber}`).querySelectorAll('option')].map(
        (option) => option.textContent,
      );

    expect(options(1)).toEqual(['Alimentação', 'Outros', 'Transferência']);
    expect(options(2)).toEqual(['Outras receitas', 'Transferência']);
  });
});
