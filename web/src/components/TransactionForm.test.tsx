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
  },
];

const categories: Category[] = [
  {
    id: 'cat-expense',
    name: 'Food',
    kind: 'Expense',
    parentId: null,
    createdAt: '2026-09-01T00:00:00Z',
  },
  {
    id: 'cat-income',
    name: 'Salary',
    kind: 'Income',
    parentId: null,
    createdAt: '2026-09-01T00:00:00Z',
  },
];

async function fillAndSubmit(amount: string, categoryId: string) {
  const onSubmit = vi.fn();
  const user = userEvent.setup();

  render(
    <TransactionForm
      accounts={accounts}
      categories={categories}
      submitLabel="Save"
      onSubmit={onSubmit}
    />,
  );

  await user.selectOptions(screen.getByLabelText('Account'), 'acc-1');
  await user.selectOptions(screen.getByLabelText('Category'), categoryId);
  await user.clear(screen.getByLabelText('Amount'));
  await user.type(screen.getByLabelText('Amount'), amount);
  await user.clear(screen.getByLabelText('Date'));
  await user.type(screen.getByLabelText('Date'), '2026-09-13');
  await user.type(screen.getByLabelText('Description'), 'Supermarket');
  await user.click(screen.getByRole('button', { name: 'Save' }));

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
      description: 'Supermarket',
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

    expect(await screen.findByText(/zero/i)).toBeInTheDocument();
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
        submitLabel="Save"
        onSubmit={vi.fn()}
      />,
    );

    const groups = screen
      .getByLabelText('Category')
      .querySelectorAll('optgroup');

    expect([...groups].map((group) => group.label)).toEqual(['Income', 'Expense']);
  });
});
