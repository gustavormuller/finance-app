import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';

import { api, type Category, type CategoryInput, type CategoryKind } from '@/api/finance';
import Alert from '@/components/Alert';
import { categoryKindLabels, categoryKindPlurals } from '@/lib/labels';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';

const selectClasses =
  'border-input dark:bg-input/30 h-9 w-full rounded-md border bg-transparent px-3 py-1 ' +
  'text-base shadow-xs outline-none md:text-sm';

/** Parents in name order, each followed by its own children. Two levels, so no recursion. */
function asTree(categories: Category[], kind: CategoryKind) {
  const ofKind = categories.filter((category) => category.kind === kind);
  const parents = ofKind
    .filter((category) => category.parentId === null)
    .sort((a, b) => a.name.localeCompare(b.name));

  return parents.map((parent) => ({
    parent,
    children: ofKind
      .filter((category) => category.parentId === parent.id)
      .sort((a, b) => a.name.localeCompare(b.name)),
  }));
}

export default function CategoriesPage() {
  const queryClient = useQueryClient();
  const [editing, setEditing] = useState<Category | null>(null);
  const [creating, setCreating] = useState(false);
  const [failure, setFailure] = useState<string | null>(null);

  const categories = useQuery({ queryKey: ['categories'], queryFn: api.listCategories });

  const close = () => {
    setCreating(false);
    setEditing(null);
    setFailure(null);
  };

  const save = useMutation({
    mutationFn: (input: CategoryInput) =>
      editing ? api.updateCategory(editing.id, input) : api.createCategory(input),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['categories'] });
      close();
    },
    onError: (error: Error) => setFailure(error.message),
  });

  const remove = useMutation({
    mutationFn: (id: string) => api.deleteCategory(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['categories'] }),
    onError: (error: Error) => setFailure(error.message),
  });

  // Only top-level categories can be a parent, which is how the two-level rule is kept
  // out of the user's way rather than enforced by rejecting them afterwards.
  const possibleParents = (categories.data ?? []).filter((category) => category.parentId === null);

  return (
    <section className="mx-auto max-w-5xl px-4 py-8">
      <div className="mb-6 flex items-center justify-between gap-4">
        <h2 className="text-2xl font-semibold tracking-tight">Categorias</h2>

        {!creating && !editing && <Button onClick={() => setCreating(true)}>Nova categoria</Button>}
      </div>

      {(creating || editing) && (
        <form
          className="border-border mb-8 grid gap-4 border-b pb-8 sm:grid-cols-3"
          onSubmit={(event) => {
            event.preventDefault();
            const form = new FormData(event.currentTarget);
            const parentId = String(form.get('parentId') ?? '');

            save.mutate({
              name: String(form.get('name') ?? ''),
              kind: String(form.get('kind') ?? 'Expense') as CategoryKind,
              parentId: parentId === '' ? null : parentId,
            });
          }}
        >
          <div className="grid gap-2">
            <Label htmlFor="category-name">Nome</Label>
            <Input id="category-name" name="name" defaultValue={editing?.name ?? ''} required />
          </div>

          <div className="grid gap-2">
            <Label htmlFor="category-kind">Tipo</Label>
            <select
              id="category-kind"
              name="kind"
              className={selectClasses}
              defaultValue={editing?.kind ?? 'Expense'}
            >
              <option value="Income">{categoryKindLabels.Income}</option>
              <option value="Expense">{categoryKindLabels.Expense}</option>
            </select>
          </div>

          <div className="grid gap-2">
            <Label htmlFor="category-parent">Categoria mãe</Label>
            <select
              id="category-parent"
              name="parentId"
              className={selectClasses}
              defaultValue={editing?.parentId ?? ''}
            >
              <option value="">Nenhuma (nível principal)</option>
              {possibleParents
                .filter((parent) => parent.id !== editing?.id)
                .map((parent) => (
                  <option key={parent.id} value={parent.id}>
                    {parent.name} ({categoryKindLabels[parent.kind]})
                  </option>
                ))}
            </select>
          </div>

          <div className="flex gap-2 sm:col-span-3">
            <Button type="submit" disabled={save.isPending}>
              {editing ? 'Salvar categoria' : 'Criar categoria'}
            </Button>
            <Button type="button" variant="outline" onClick={close}>
              Cancelar
            </Button>
          </div>
        </form>
      )}

      {failure && <Alert>{failure}</Alert>}

      {(['Income', 'Expense'] as const).map((kind) => (
        <div key={kind} className="mb-8">
          <h3 className="text-muted-foreground mb-2 text-xs font-semibold tracking-[0.1em] uppercase">
            {categoryKindPlurals[kind]}
          </h3>

          <ul className="border-border border-t">
            {asTree(categories.data ?? [], kind).map(({ parent, children }) => (
              <li key={parent.id}>
                <Row category={parent} onEdit={setEditing} onDelete={remove.mutate} />

                {children.map((child) => (
                  <Row
                    key={child.id}
                    category={child}
                    indented
                    onEdit={setEditing}
                    onDelete={remove.mutate}
                  />
                ))}
              </li>
            ))}
          </ul>
        </div>
      ))}
    </section>
  );
}

function Row({
  category,
  indented,
  onEdit,
  onDelete,
}: {
  category: Category;
  indented?: boolean;
  onEdit: (category: Category) => void;
  onDelete: (id: string) => void;
}) {
  return (
    <div
      className={`border-border flex items-center justify-between border-b py-2 ${
        indented ? 'pl-8' : ''
      }`}
    >
      <span className={indented ? 'text-muted-foreground' : 'font-medium'}>{category.name}</span>

      <span className="flex gap-1">
        <Button variant="ghost" size="sm" onClick={() => onEdit(category)}>
          Editar
        </Button>
        <Button variant="ghost" size="sm" onClick={() => onDelete(category.id)}>
          Excluir
        </Button>
      </span>
    </div>
  );
}
