import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ChevronRight, Plus } from 'lucide-react';
import { Fragment, useState } from 'react';

import { api, type Category, type CategoryInput, type CategoryKind } from '@/api/finance';
import Alert from '@/components/Alert';
import { Actions, FormRow, UseCells } from '@/components/categories/CategoryCells';
import CategoryForm from '@/components/categories/CategoryForm';
import PageHeader from '@/components/PageHeader';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { categoryTree, fold, type Main, type Use } from '@/lib/categoryTree';
import { categoryKindPlurals, categoryKinds } from '@/lib/labels';
import { cn } from '@/lib/utils';

type KindFilter = 'All' | CategoryKind;

/** The one open form: a new category above the table, or under the row it belongs to. */
type Form = { mode: 'create'; parent: Category | null } | { mode: 'edit'; category: Category };

const KIND_FILTERS: [KindFilter, string][] = [
  ['All', 'Todas'],
  ['Expense', 'Despesas'],
  ['Income', 'Receitas'],
  ['Transfer', 'Transferências'],
];

/**
 * `/categories` (013): one table per kind with each category's use over the last 12
 * months, main categories and their subcategories in one tree, and every edit on the
 * row being edited — the form opens under it, never at the top of the page.
 */
export default function CategoriesPage() {
  const queryClient = useQueryClient();
  const [kind, setKind] = useState<KindFilter>('All');
  const [search, setSearch] = useState('');
  const [unusedOnly, setUnusedOnly] = useState(false);
  const [opened, setOpened] = useState<ReadonlySet<string>>(new Set());
  const [form, setForm] = useState<Form | null>(null);
  const [failure, setFailure] = useState<string | null>(null);

  const categories = useQuery({ queryKey: ['categories'], queryFn: api.listCategories });
  const usage = useQuery({ queryKey: ['categories', 'usage'], queryFn: api.categoryUsage });

  const close = () => {
    setForm(null);
    setFailure(null);
  };
  const open = (next: Form) => {
    setFailure(null);
    setForm(next);
  };

  const save = useMutation({
    mutationFn: (input: CategoryInput) =>
      form?.mode === 'edit' ? api.updateCategory(form.category.id, input) : api.createCategory(input),
    onSuccess: async (_, input) => {
      await queryClient.invalidateQueries({ queryKey: ['categories'] });
      if (input.parentId) {
        const parentId = input.parentId;
        setOpened((current) => new Set(current).add(parentId));
      }
      close();
    },
    onError: (error: Error) => setFailure(error.message),
  });

  const remove = useMutation({
    mutationFn: (id: string) => api.deleteCategory(id),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['categories'] });
      close();
    },
    onError: (error: Error) => setFailure(error.message),
  });

  const all = categories.data ?? [];
  const mains = all.filter((category) => category.parentId === null);
  const query = fold(search.trim());
  const filtering = query !== '' || unusedOnly;
  const matches = (category: Category) => query === '' || fold(category.name).includes(query);
  const unused = (use: Use) => use.count === 0;

  const trees = Object.fromEntries(categoryKinds.map((k) => [k, categoryTree(all, usage.data ?? [], k)])) as Record<CategoryKind, Main[]>;
  const unusedCount = categoryKinds
    .flatMap((k) => trees[k].flatMap((main) => [main.rollup, ...main.children.map((child) => child.use)]))
    .filter(unused).length;

  const formProps = {
    mains,
    pending: save.isPending,
    onSubmit: (input: CategoryInput) => save.mutate(input),
    onCancel: close,
  };

  return (
    <section className="grid gap-6 [&>*]:min-w-0">
      <PageHeader
        title="Categorias"
        subtitle="Uso nos últimos 12 meses. Clique numa categoria para ver as subcategorias."
        actions={
          <Button onClick={() => open({ mode: 'create', parent: null })}>
            <Plus aria-hidden="true" />
            Nova categoria
          </Button>
        }
      />

      <div className="flex flex-wrap items-center gap-3">
        <Input
          type="search"
          aria-label="Buscar categoria"
          placeholder="Buscar categoria ou subcategoria"
          value={search}
          onChange={(event) => setSearch(event.target.value)}
          className="w-full sm:w-80"
        />
        <div role="group" aria-label="Filtrar por tipo" className="bg-secondary flex gap-0.5 rounded-xl p-0.5">
          {KIND_FILTERS.map(([value, text]) => (
            <Button
              key={value}
              size="sm"
              variant="ghost"
              aria-pressed={kind === value}
              onClick={() => setKind(value)}
              className="aria-pressed:bg-card aria-pressed:text-foreground text-muted-foreground aria-pressed:shadow-sm"
            >
              {text}
            </Button>
          ))}
        </div>
        <button
          type="button"
          role="switch"
          aria-checked={unusedOnly}
          onClick={() => setUnusedOnly((current) => !current)}
          className="text-muted-foreground flex items-center gap-2 text-sm font-semibold sm:ml-auto"
        >
          <span className={cn('relative h-5 w-9 rounded-full transition-colors', unusedOnly ? 'bg-primary' : 'bg-secondary')}>
            <span
              className={cn(
                'bg-card absolute top-0.5 size-4 rounded-full shadow transition-all',
                unusedOnly ? 'left-[1.125rem]' : 'left-0.5',
              )}
            />
          </span>
          Só as sem uso ({unusedCount})
        </button>
      </div>

      {failure && <Alert>{failure}</Alert>}

      {form?.mode === 'create' && form.parent === null && (
        <div className="glass rounded-2xl p-5 sm:p-6">
          <CategoryForm {...formProps} initial={{ name: '', kind: kind === 'All' ? 'Expense' : kind, parentId: null }} />
        </div>
      )}

      {(kind === 'All' ? categoryKinds : [kind]).map((k) => {
        const mainsOfKind = trees[k];
        const kindTotal = mainsOfKind.reduce((sum, main) => sum + Math.abs(main.rollup.total), 0);
        const share = (use: Use) => (k === 'Transfer' || kindTotal === 0 ? null : Math.abs(use.total) / kindTotal);

        const rows = mainsOfKind.flatMap((main) => {
          // A subcategory shows when it passes the filters, or when its main category
          // matched the search by name; a main category shows when it passes itself,
          // or as the context of a subcategory that does.
          const passing = main.children.filter(
            (child) => (matches(child.category) || (query !== '' && matches(main.category))) && (!unusedOnly || unused(child.use)),
          );
          const selfShown = matches(main.category) && (!unusedOnly || unused(main.rollup));
          const forced = filtering && passing.length > 0;
          if (!selfShown && !forced) {
            return [];
          }

          const formInside =
            form?.mode === 'create' ? form.parent?.id === main.category.id : form?.category.parentId === main.category.id;
          return [
            {
              main,
              children: filtering ? passing : main.children,
              isOpen: opened.has(main.category.id) || forced || formInside,
            },
          ];
        });

        if (rows.length === 0 && filtering) {
          return null;
        }

        return (
          <section key={k} aria-labelledby={`kind-${k}`} className="grid gap-2">
            <h3 id={`kind-${k}`} className="text-muted-foreground font-sans text-sm font-semibold">
              {categoryKindPlurals[k]}
            </h3>

            {rows.length === 0 ? (
              <p className="text-muted-foreground glass rounded-2xl px-4 py-3 text-sm">Nenhuma categoria deste tipo.</p>
            ) : (
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Categoria</TableHead>
                    <TableHead className="text-right">Lanç. 12m</TableHead>
                    <TableHead className="text-right">Total 12m</TableHead>
                    <TableHead className="hidden md:table-cell">Participação</TableHead>
                    <TableHead>
                      <span className="sr-only">Ações</span>
                    </TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {rows.map(({ main, children, isOpen }) => (
                    <Fragment key={main.category.id}>
                      <TableRow>
                        <TableCell>
                          {main.children.length > 0 ? (
                            <button
                              type="button"
                              aria-expanded={isOpen}
                              onClick={() =>
                                setOpened((current) => {
                                  const next = new Set(current);
                                  if (next.has(main.category.id)) {
                                    next.delete(main.category.id);
                                  } else {
                                    next.add(main.category.id);
                                  }
                                  return next;
                                })
                              }
                              className="flex items-center gap-2 font-semibold"
                            >
                              <ChevronRight
                                aria-hidden="true"
                                className={cn('text-muted-foreground size-4 transition-transform', isOpen && 'rotate-90')}
                              />
                              <span data-testid="category-name">{main.category.name}</span>
                              <span className="bg-secondary text-muted-foreground rounded-full px-2 text-xs">
                                {main.children.length}
                              </span>
                            </button>
                          ) : (
                            <span className="flex items-center gap-2 pl-6 font-semibold">
                              <span data-testid="category-name">{main.category.name}</span>
                            </span>
                          )}
                        </TableCell>
                        <UseCells use={main.rollup} share={share(main.rollup)} />
                        <TableCell>
                          <Actions
                            name={main.category.name}
                            onSub={() => open({ mode: 'create', parent: main.category })}
                            onEdit={() => open({ mode: 'edit', category: main.category })}
                            onDelete={() => remove.mutate(main.category.id)}
                          />
                        </TableCell>
                      </TableRow>

                      {form?.mode === 'edit' && form.category.id === main.category.id && (
                        <FormRow>
                          <CategoryForm
                            {...formProps}
                            initial={main.category}
                            editing={main.category}
                            onDelete={() => remove.mutate(main.category.id)}
                          />
                        </FormRow>
                      )}

                      {isOpen &&
                        children.map((child) => (
                          <Fragment key={child.category.id}>
                            <TableRow>
                              <TableCell className="pl-12">
                                <span data-testid="category-name">{child.category.name}</span>
                              </TableCell>
                              <UseCells use={child.use} share={share(child.use)} />
                              <TableCell>
                                <Actions
                                  name={child.category.name}
                                  onEdit={() => open({ mode: 'edit', category: child.category })}
                                  onDelete={() => remove.mutate(child.category.id)}
                                />
                              </TableCell>
                            </TableRow>
                            {form?.mode === 'edit' && form.category.id === child.category.id && (
                              <FormRow>
                                <CategoryForm
                                  {...formProps}
                                  initial={child.category}
                                  editing={child.category}
                                  onDelete={() => remove.mutate(child.category.id)}
                                />
                              </FormRow>
                            )}
                          </Fragment>
                        ))}

                      {form?.mode === 'create' && form.parent?.id === main.category.id && (
                        <FormRow>
                          <CategoryForm {...formProps} initial={{ name: '', kind: main.category.kind, parentId: main.category.id }} />
                        </FormRow>
                      )}
                    </Fragment>
                  ))}
                </TableBody>
              </Table>
            )}
          </section>
        );
      })}
    </section>
  );
}
