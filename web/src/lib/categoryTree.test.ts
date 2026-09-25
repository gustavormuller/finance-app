import { describe, expect, it } from 'vitest';

import type { Category } from '@/api/finance';

import { categoryTree, fold } from './categoryTree';

const at = '2026-09-01T00:00:00Z';
const category = (id: string, name: string, parentId: string | null = null, kind: Category['kind'] = 'Expense'): Category => ({
  id,
  name,
  kind,
  parentId,
  createdAt: at,
});

describe('categoryTree', () => {
  const categories = [
    category('home', 'Moradia'),
    category('rent', 'Aluguel', 'home'),
    category('condo', 'Condomínio', 'home'),
    category('food', 'Alimentação'),
    category('salary', 'Salário', null, 'Income'),
  ];

  /** Spec 013 decision 3: a main category's row shows its own use plus its subcategories'. */
  it('rolls subcategories up into their main category', () => {
    const usage = [
      { categoryId: 'home', count: 1, total: -50 },
      { categoryId: 'rent', count: 3, total: -3000 },
    ];

    const home = categoryTree(categories, usage, 'Expense')[0]!;

    expect(home.category.id).toBe('home');
    expect(home.rollup).toEqual({ count: 4, total: -3050 });
    expect(home.children.map((child) => [child.category.id, child.use.count])).toEqual([
      ['rent', 3],
      ['condo', 0],
    ]);
  });

  it('puts the largest use first, then orders by name', () => {
    const usage = [{ categoryId: 'food', count: 2, total: -1000 }];

    expect(categoryTree(categories, usage, 'Expense').map((main) => main.category.name)).toEqual(['Alimentação', 'Moradia']);
    expect(categoryTree(categories, [], 'Expense').map((main) => main.category.name)).toEqual(['Alimentação', 'Moradia']);
  });

  it('keeps each kind to itself', () => {
    expect(categoryTree(categories, [], 'Income').map((main) => main.category.id)).toEqual(['salary']);
    expect(categoryTree(categories, [], 'Transfer')).toEqual([]);
  });
});

describe('fold', () => {
  it('ignores case and accents', () => {
    expect(fold('Alimentação')).toBe(fold('ALIMENTACAO'));
  });
});
