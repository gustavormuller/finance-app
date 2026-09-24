import { render, screen, within } from '@testing-library/react';
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
    categorySource: 'Default',
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

function renderStep(rows: StagedRow[], { aiEnabled = true }: { aiEnabled?: boolean } = {}) {
  const onPatchRow = vi.fn();
  const onCommit = vi.fn();
  const onSuggest = vi.fn();

  render(
    <PreviewStep
      detail={detail(rows)}
      categories={categories}
      filter=""
      busy={false}
      error={null}
      notice={null}
      aiEnabled={aiEnabled}
      onSuggest={onSuggest}
      onFilter={vi.fn()}
      onPage={vi.fn()}
      onPatchRow={onPatchRow}
      onCommit={onCommit}
      onDiscard={vi.fn()}
    />,
  );

  return { onPatchRow, onCommit, onSuggest };
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

  /**
   * A Transfer category takes any sign (005 amendment 1), so Transferência is offered
   * next to the kind the sign implies, on a leaving row and an arriving one alike.
   */
  it('offers only categories of the kind that agrees with the sign, plus Transfer', () => {
    renderStep([
      row({ id: 'r1', rowNumber: 1, status: 'Ready', amount: -10 }),
      row({ id: 'r2', rowNumber: 2, status: 'Ready', amount: 10, categoryId: 'cat-income' }),
    ]);

    const expense = screen.getByRole('combobox', { name: 'Categoria da linha 1' });
    const income = screen.getByRole('combobox', { name: 'Categoria da linha 2' });

    expect(Array.from(expense.querySelectorAll('option')).map((option) => option.textContent)).toEqual([
      'Alimentação',
      'Outros',
      'Transferência',
    ]);
    expect(Array.from(income.querySelectorAll('option')).map((option) => option.textContent)).toEqual([
      'Outras receitas',
      'Transferência',
    ]);
  });

  /** Spec web unit test 25. With AI off the button stays, disabled, and says why. */
  it('disables "Sugerir com IA" with the reason when AI is off', () => {
    const { onSuggest } = renderStep([row({ id: 'r1', rowNumber: 1, status: 'Ready' })], { aiEnabled: false });

    const suggest = screen.getByRole('button', { name: 'Sugerir com IA' });
    expect(suggest).toBeDisabled();
    expect(suggest).toHaveAccessibleDescription(
      'A IA está desligada na sua conta. Ligue-a em Configurações para receber sugestões.',
    );

    suggest.click();
    expect(onSuggest).not.toHaveBeenCalled();
  });

  it('offers "Sugerir com IA" when AI is on', async () => {
    const { onSuggest } = renderStep([row({ id: 'r1', rowNumber: 1, status: 'Ready' })]);

    const suggest = screen.getByRole('button', { name: 'Sugerir com IA' });
    expect(suggest).toBeEnabled();
    expect(suggest).not.toHaveAccessibleDescription();

    await userEvent.setup().click(suggest);
    expect(onSuggest).toHaveBeenCalledTimes(1);
  });

  it('marks a row whose category the AI suggested, and no other', () => {
    renderStep([
      row({ id: 'r1', rowNumber: 1, status: 'Ready', categoryId: 'cat-food', categorySource: 'Ai' }),
      row({ id: 'r2', rowNumber: 2, status: 'Ready', categorySource: 'Default' }),
      row({ id: 'r3', rowNumber: 3, status: 'Ready', categorySource: 'History' }),
    ]);

    const [first, second, third] = screen.getAllByTestId('staged-row-Ready');
    expect(within(first!).getByTestId('ai-marker')).toHaveAccessibleName('Sugerida pela IA');
    expect(within(first!).getByRole('combobox', { name: 'Categoria da linha 1' })).toHaveValue('cat-food');
    expect(within(second!).queryByTestId('ai-marker')).not.toBeInTheDocument();
    expect(within(third!).queryByTestId('ai-marker')).not.toBeInTheDocument();
  });
});
