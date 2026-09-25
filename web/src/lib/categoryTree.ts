import type { Category, CategoryKind, CategoryUsage } from '@/api/finance';

/** A category's transactions in the range: how many, and their signed sum. */
export type Use = { count: number; total: number };
export type Leaf = { category: Category; use: Use };
/** A main category; its rollup is its own use plus its subcategories'. */
export type Main = { category: Category; rollup: Use; children: Leaf[] };

const NONE: Use = { count: 0, total: 0 };

/** Case- and accent-insensitive, so "alimentacao" finds Alimentação. */
export const fold = (text: string) => text.normalize('NFD').replace(/\p{Diacritic}/gu, '').toLowerCase();

const size = (entry: Leaf | Main) => Math.abs('rollup' in entry ? entry.rollup.total : entry.use.total);

/** Largest use first, then by name: the categories that matter float up. */
const bySize = (a: Leaf | Main, b: Leaf | Main) => size(b) - size(a) || a.category.name.localeCompare(b.category.name);

/** Main categories of a kind with their subcategories, each with its use (spec 013). */
export function categoryTree(categories: Category[], usage: CategoryUsage[], kind: CategoryKind): Main[] {
  const used = new Map(usage.map((entry) => [entry.categoryId, { count: entry.count, total: entry.total }]));
  const use = (id: string) => used.get(id) ?? NONE;
  const ofKind = categories.filter((category) => category.kind === kind);

  return ofKind
    .filter((category) => category.parentId === null)
    .map((category) => {
      const children = ofKind
        .filter((child) => child.parentId === category.id)
        .map((child) => ({ category: child, use: use(child.id) }))
        .sort(bySize);
      const rollup = children.reduce(
        (sum, child) => ({ count: sum.count + child.use.count, total: sum.total + child.use.total }),
        use(category.id),
      );
      return { category, rollup, children };
    })
    .sort(bySize);
}
