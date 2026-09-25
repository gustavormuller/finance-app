import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';

import type { Category, CategoryUsage } from '@/api/finance';
import { categoryKindLabels } from '@/lib/labels';
import { renderWithClient, stubFetch, type SeenRequest } from '@/test-utils';

import CategoriesPage from './CategoriesPage';

const at = '2026-09-01T00:00:00Z';
const transfer: Category = { id: 'cat-transfer', name: 'Transferência', kind: 'Transfer', parentId: null, createdAt: at };
const home: Category = { id: 'home', name: 'Moradia', kind: 'Expense', parentId: null, createdAt: at };
const rent: Category = { id: 'rent', name: 'Aluguel', kind: 'Expense', parentId: 'home', createdAt: at };
const condo: Category = { id: 'condo', name: 'Condomínio', kind: 'Expense', parentId: 'home', createdAt: at };
const food: Category = { id: 'food', name: 'Alimentação', kind: 'Expense', parentId: null, createdAt: at };
const salary: Category = { id: 'salary', name: 'Salário', kind: 'Income', parentId: null, createdAt: at };
const tree = [home, rent, condo, food, salary];

// Moradia: 3 000 of 4 000 spent, all of it in Aluguel; Condomínio and Salário unused.
const usage: CategoryUsage[] = [
  { categoryId: 'rent', count: 3, total: -3000 },
  { categoryId: 'food', count: 2, total: -1000 },
];

function stubTree(categories: Category[] = tree, used: CategoryUsage[] = usage) {
  return stubFetch((request: SeenRequest) => {
    const path = new URL(request.url, 'http://localhost').pathname;

    if (request.method === 'POST') return { status: 201, body: { ...rent, id: 'new' } };
    if (path === '/api/categories/usage') return { body: used };
    if (path === '/api/categories') return { body: categories };
    return undefined;
  });
}

/** The table row that holds a category's name. */
const rowOf = (name: string) => screen.getByText(name, { selector: '[data-testid=category-name]' }).closest('tr')!;

describe('CategoriesPage', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  /** 005 amendment 1: the wire member is English, the screen reads Portuguese. */
  it('labels the Transfer kind in Portuguese', () => {
    expect(categoryKindLabels.Transfer).toBe('Transferência');
  });

  /** 005 amendment 1: existing users create Transferência by hand, so the form must offer it. */
  it('creates a Transfer category', async () => {
    const user = userEvent.setup();
    const seen = stubTree([], []);

    renderWithClient(<CategoriesPage />);

    await user.click(screen.getByRole('button', { name: 'Nova categoria' }));
    await user.type(screen.getByLabelText('Nome'), 'Transferência');
    await user.selectOptions(screen.getByLabelText('Tipo'), 'Transferência');
    await user.click(screen.getByRole('button', { name: 'Criar categoria' }));

    await waitFor(() =>
      expect(seen.find((request) => request.method === 'POST')?.body).toEqual({
        name: 'Transferência',
        kind: 'Transfer',
        parentId: null,
      }),
    );
  });

  /** A seeded Transferência must be listed, not silently hidden by a two-kind page. */
  it('lists Transfer categories under their own heading', async () => {
    stubTree([transfer], []);

    renderWithClient(<CategoriesPage />);

    expect(await screen.findByText('Transferência')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Transferências' })).toBeInTheDocument();
  });

  /** Spec 013 test 5: a main category carries its subcategories' use. */
  it('lists main categories with their 12-month count, total and share', async () => {
    stubTree();
    renderWithClient(<CategoriesPage />);

    await screen.findByText('Moradia');
    const row = rowOf('Moradia');

    expect(within(row).getByTestId('category-count')).toHaveTextContent('3');
    expect(within(row).getByTestId('amount')).toHaveTextContent('−3.000,00');
    expect(within(row).getByTestId('category-share')).toHaveTextContent('75,0%');
  });

  /** Spec 013 test 6. */
  it('shows the subcategories when a main category is opened', async () => {
    const user = userEvent.setup();
    stubTree();
    renderWithClient(<CategoriesPage />);

    await screen.findByText('Moradia');
    expect(screen.queryByText('Aluguel')).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: /^Moradia/ }));

    expect(screen.getByText('Aluguel')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /^Moradia/ })).toHaveAttribute('aria-expanded', 'true');
  });

  /** Spec 013 test 7: the form opens where the category is, not at the top of the page. */
  it('opens the edit form right after the edited category', async () => {
    const user = userEvent.setup();
    stubTree();
    renderWithClient(<CategoriesPage />);

    await user.click(await screen.findByRole('button', { name: /^Moradia/ }));
    await user.click(screen.getByRole('button', { name: 'Editar Aluguel' }));

    const formRow = rowOf('Aluguel').nextElementSibling as HTMLElement;
    expect(within(formRow).getByLabelText('Nome')).toHaveValue('Aluguel');
    expect(within(formRow).getByLabelText('Fica dentro de')).toHaveValue('home');
    expect(screen.getAllByLabelText('Nome')).toHaveLength(1);
  });

  /** Spec 013 test 8. */
  it('creates a subcategory with its main category already chosen', async () => {
    const user = userEvent.setup();
    const seen = stubTree();
    renderWithClient(<CategoriesPage />);

    await screen.findByText('Moradia');
    await user.click(screen.getByRole('button', { name: 'Nova subcategoria em Moradia' }));

    expect(screen.getByLabelText('Fica dentro de')).toHaveValue('home');

    await user.type(screen.getByLabelText('Nome'), 'Garagem');
    await user.click(screen.getByRole('button', { name: 'Criar categoria' }));

    await waitFor(() =>
      expect(seen.find((request) => request.method === 'POST')?.body).toEqual({
        name: 'Garagem',
        kind: 'Expense',
        parentId: 'home',
      }),
    );
  });

  /** Spec 013 test 9. */
  it('leaves only unused categories when asked', async () => {
    const user = userEvent.setup();
    stubTree();
    renderWithClient(<CategoriesPage />);

    await screen.findByText('Moradia');
    await user.click(screen.getByRole('switch', { name: /Só as sem uso/ }));

    expect(screen.getByText('Condomínio')).toBeInTheDocument();
    expect(screen.getByText('Salário')).toBeInTheDocument();
    expect(screen.queryByText('Alimentação')).not.toBeInTheDocument();
    expect(screen.queryByText('Aluguel')).not.toBeInTheDocument();
  });

  /** Spec 013 test 10. */
  it('keeps a main category whose subcategory matches the search', async () => {
    const user = userEvent.setup();
    stubTree();
    renderWithClient(<CategoriesPage />);

    await screen.findByText('Moradia');
    await user.type(screen.getByRole('searchbox', { name: 'Buscar categoria' }), 'alug');

    expect(screen.getByText('Moradia')).toBeInTheDocument();
    expect(screen.getByText('Aluguel')).toBeInTheDocument();
    expect(screen.queryByText('Alimentação')).not.toBeInTheDocument();
  });
});
