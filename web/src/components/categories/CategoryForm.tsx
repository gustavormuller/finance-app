import { useState } from 'react';

import type { Category, CategoryInput, CategoryKind } from '@/api/finance';
import { selectClasses } from '@/components/FormField';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { categoryKindLabels, categoryKinds } from '@/lib/labels';

/**
 * Nome, Tipo and "Fica dentro de" — the plain words for a category's place.
 * Choosing a main category fixes the kind to that category's, which is the rule the API
 * enforces.
 */
export default function CategoryForm({
  initial,
  editing,
  mains,
  pending,
  onSubmit,
  onCancel,
  onDelete,
}: {
  initial: Pick<Category, 'name' | 'kind' | 'parentId'>;
  editing?: Category;
  mains: Category[];
  pending: boolean;
  onSubmit: (input: CategoryInput) => void;
  onCancel: () => void;
  onDelete?: () => void;
}) {
  const [name, setName] = useState(initial.name);
  const [kind, setKind] = useState<CategoryKind>(initial.kind);
  const [parentId, setParentId] = useState(initial.parentId ?? '');
  const parent = mains.find((main) => main.id === parentId);

  return (
    <form
      className="grid gap-4 sm:grid-cols-[minmax(0,1.3fr)_minmax(0,1fr)_minmax(0,1.3fr)] sm:items-end"
      onSubmit={(event) => {
        event.preventDefault();
        onSubmit({ name, kind: parent?.kind ?? kind, parentId: parent ? parent.id : null });
      }}
    >
      <div className="grid gap-2">
        <Label htmlFor="category-name">Nome</Label>
        <Input id="category-name" value={name} onChange={(event) => setName(event.target.value)} required />
      </div>

      <div className="grid gap-2">
        <Label htmlFor="category-kind">Tipo</Label>
        <select
          id="category-kind"
          className={selectClasses}
          value={parent?.kind ?? kind}
          disabled={parent !== undefined}
          onChange={(event) => setKind(event.target.value as CategoryKind)}
        >
          {categoryKinds.map((option) => (
            <option key={option} value={option}>
              {categoryKindLabels[option]}
            </option>
          ))}
        </select>
      </div>

      <div className="grid gap-2">
        <Label htmlFor="category-parent">Fica dentro de</Label>
        <select
          id="category-parent"
          className={selectClasses}
          value={parentId}
          onChange={(event) => setParentId(event.target.value)}
        >
          <option value="">Nenhuma — é uma categoria principal</option>
          {mains
            .filter((main) => main.id !== editing?.id)
            .map((main) => (
              <option key={main.id} value={main.id}>
                {main.name} ({categoryKindLabels[main.kind]})
              </option>
            ))}
        </select>
      </div>

      <div className="flex flex-wrap gap-2 sm:col-span-3">
        <Button type="submit" disabled={pending}>
          {editing ? 'Salvar' : 'Criar categoria'}
        </Button>
        <Button type="button" variant="outline" onClick={onCancel}>
          Cancelar
        </Button>
        {editing && onDelete && (
          <Button type="button" variant="ghost" className="text-destructive ml-auto" onClick={onDelete}>
            Excluir
          </Button>
        )}
      </div>
    </form>
  );
}
