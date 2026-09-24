import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import type { Account, Category } from '@/api/finance';

import TransactionForm from './TransactionForm';

const accounts: Account[] = [
  {
    id: 'acc-1',
    name: 'Nubank',
    type: 'Checking',
    currency: 'BRL',
    createdAt: '2026-09-01T00:00:00Z',
    openingBalance: 0,
  },
];

const categories: Category[] = [
  {
    id: 'cat-expense',
    name: 'Alimentação',
    kind: 'Expense',
    parentId: null,
    createdAt: '2026-09-01T00:00:00Z',
  },
  {
    id: 'cat-income',
    name: 'Salário',
    kind: 'Income',
    parentId: null,
    createdAt: '2026-09-01T00:00:00Z',
  },
  {
    id: 'cat-transfer',
    name: 'Transferência',
    kind: 'Transfer',
    parentId: null,
    createdAt: '2026-09-01T00:00:00Z',
  },
];

async function fillAndSubmit(amount: string, categoryId: string, direction?: 'Saída' | 'Entrada') {
  const onSubmit = vi.fn();
  const user = userEvent.setup();

  render(
    <TransactionForm
      accounts={accounts}
      categories={categories}
      submitLabel="Salvar"
      onSubmit={onSubmit}
    />,
  );

  await user.selectOptions(screen.getByLabelText('Conta'), 'acc-1');
  await user.selectOptions(screen.getByLabelText('Categoria'), categoryId);
  await user.clear(screen.getByLabelText('Valor'));
  await user.type(screen.getByLabelText('Valor'), amount);
  await user.clear(screen.getByLabelText('Data'));
  await user.type(screen.getByLabelText('Data'), '2026-09-13');
  await user.type(screen.getByLabelText('Descrição'), 'Supermercado');
  if (direction) {
    await user.click(screen.getByRole('radio', { name: direction }));
  }
  await user.click(screen.getByRole('button', { name: 'Salvar' }));

  return onSubmit;
}

describe('TransactionForm', () => {
  /**
   * Spec web unit test 1. The user types 42.90 and picks Food; what leaves the form
   * is −42.90. They never type a minus sign, and they cannot file an expense as
   * income by forgetting one.
   */
  it('submits a negative amount for an expense category', async () => {
    const onSubmit = await fillAndSubmit('42.90', 'cat-expense');

    await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1));

    expect(onSubmit.mock.calls[0]?.[0]).toMatchObject({
      accountId: 'acc-1',
      categoryId: 'cat-expense',
      amount: -42.9,
      currency: 'BRL',
      date: '2026-09-13',
      description: 'Supermercado',
    });
  });

  /** Spec web unit test 2. */
  it('submits a positive amount for an income category', async () => {
    const onSubmit = await fillAndSubmit('3000.00', 'cat-income');

    await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1));

    expect(onSubmit.mock.calls[0]?.[0]).toMatchObject({
      categoryId: 'cat-income',
      amount: 3000,
    });
  });

  /**
   * Spec web unit test 3. Zero is refused in the browser as well as at the API, so
   * the user is told before a round trip rather than after one.
   */
  it('refuses a zero amount and does not submit', async () => {
    const onSubmit = await fillAndSubmit('0', 'cat-expense');

    expect(await screen.findByText(/não pode ser zero/i)).toBeInTheDocument();
    expect(onSubmit).not.toHaveBeenCalled();
  });

  /**
   * A minus sign typed anyway must not flip the meaning of the category. The sign is
   * the category's to decide, so the magnitude is what the field contributes.
   */
  it('ignores a sign typed into the amount field', async () => {
    const onSubmit = await fillAndSubmit('-42.90', 'cat-income');

    await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1));

    expect(onSubmit.mock.calls[0]?.[0]).toMatchObject({ amount: 42.9 });
  });

  /** The dropdown is grouped by kind, which is what makes the sign rule visible. */
  it('groups the category dropdown by kind', () => {
    render(
      <TransactionForm
        accounts={accounts}
        categories={categories}
        submitLabel="Salvar"
        onSubmit={vi.fn()}
      />,
    );

    const groups = screen
      .getByLabelText('Categoria')
      .querySelectorAll('optgroup');

    // The members stay English on the wire; only the reading of them is Portuguese.
    expect([...groups].map((group) => group.label)).toEqual(['Receita', 'Despesa', 'Transferência']);
  });

  /**
   * 005 amendment 1. A transfer has no direction of its own — the same category
   * leaves checking and arrives on the card — so the user picks the sign.
   */
  it.each([
    ['Saída', -500],
    ['Entrada', 500],
  ] as const)('lets the user choose the sign for a Transfer category: %s', async (direction, amount) => {
    const onSubmit = await fillAndSubmit('500', 'cat-transfer', direction);

    await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1));

    expect(onSubmit.mock.calls[0]?.[0]).toMatchObject({ categoryId: 'cat-transfer', amount });
  });

  /** For Income and Expense the kind still decides, so there is nothing to choose. */
  it('offers no direction for an Income or Expense category', async () => {
    const user = userEvent.setup();

    render(
      <TransactionForm accounts={accounts} categories={categories} submitLabel="Salvar" onSubmit={vi.fn()} />,
    );

    await user.selectOptions(screen.getByLabelText('Categoria'), 'cat-expense');
    expect(screen.queryByRole('radio', { name: 'Saída' })).not.toBeInTheDocument();

    await user.selectOptions(screen.getByLabelText('Categoria'), 'cat-transfer');
    expect(screen.getByRole('radio', { name: 'Saída' })).toBeChecked();
  });

  /** Editing an arriving transfer opens on the direction it already has. */
  it('opens an existing transfer on its own direction', () => {
    render(
      <TransactionForm
        accounts={accounts}
        categories={categories}
        submitLabel="Salvar"
        onSubmit={vi.fn()}
        defaultValues={{ categoryId: 'cat-transfer', amount: '500.00', direction: 'in' }}
      />,
    );

    expect(screen.getByRole('radio', { name: 'Entrada' })).toBeChecked();
  });
});
