import { zodResolver } from '@hookform/resolvers/zod';
import { useForm, useWatch } from 'react-hook-form';
import { z } from 'zod';

import type { Account, Category, TransactionInput } from '@/api/finance';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { categoryKindLabels, categoryKinds } from '@/lib/labels';
import { cn } from '@/lib/utils';

export interface TransactionFormValues {
  accountId: string;
  categoryId: string;
  /**
   * Unsigned, as typed. The sign comes from the category's kind, never the keyboard —
   * except for a Transfer, which has no direction of its own (005 amendment 1).
   */
  amount: string;
  /** A Transfer's sign: `out` leaves the account, `in` arrives. Ignored for other kinds. */
  direction: 'out' | 'in';
  date: string;
  description: string;
}

export interface TransactionFormProps {
  accounts: Account[];
  categories: Category[];
  defaultValues?: Partial<TransactionFormValues>;
  submitLabel: string;
  onSubmit: (input: TransactionInput) => void | Promise<void>;
  onCancel?: () => void;
}

/**
 * Parses what was typed into a magnitude.
 *
 * A comma is accepted as the decimal separator because the app formats in pt-BR and
 * people type back what they are shown. Any sign typed is discarded rather than
 * honoured — see the schema below.
 */
function parseAmount(typed: string): number {
  return Math.abs(Number(typed.replace(',', '.')));
}

/**
 * A child is shown as "Parent / Child" rather than indented with spaces: an option
 * element gives no reliable way to indent across browsers, and the path also says
 * which parent it belongs to, which indentation alone does not.
 */
function categoryLabel(category: Category, all: Category[]): string {
  if (category.parentId === null) {
    return category.name;
  }

  const parent = all.find((candidate) => candidate.id === category.parentId);

  return parent ? `${parent.name} / ${category.name}` : category.name;
}

const schema = z.object({
  accountId: z.string().min(1, 'Escolha uma conta.'),
  categoryId: z.string().min(1, 'Escolha uma categoria.'),
  amount: z
    .string()
    .min(1, 'Informe um valor.')
    .refine((typed) => Number.isFinite(parseAmount(typed)), 'Informe um número.')
    // Mirrors the API's rule 1, so the user is told before a round trip rather than
    // after one. The API still enforces it; this is a courtesy, not the guarantee.
    .refine((typed) => parseAmount(typed) !== 0, 'O valor não pode ser zero.'),
  date: z.string().min(1, 'Escolha uma data.'),
  description: z.string().trim().min(1, 'Informe uma descrição.'),
  direction: z.enum(['out', 'in']),
});

/** The class stack shadcn/ui's Input uses, so a native select sits level with one. */
const selectClasses =
  'border-input bg-transparent dark:bg-input/30 flex h-9 w-full min-w-0 rounded-md border ' +
  'px-3 py-1 text-base shadow-xs transition-[color,box-shadow] outline-none md:text-sm ' +
  'focus-visible:border-ring focus-visible:ring-ring/50 focus-visible:ring-[3px]';

/**
 * Create and edit, one form.
 *
 * The amount field takes an unsigned number and the sign is derived from the selected
 * category's kind, so "Salary: −3000" is not a mistake the interface lets you make.
 * The API enforces the same rule; this keeps the user from ever meeting it.
 * A Transfer (005) has no direction of its own, so for one the form asks: Saída or
 * Entrada.
 *
 * Native `select` rather than the Radix one: `optgroup` gives the spec's "grouped by
 * kind" for free, with the platform's own accessibility and its own mobile picker.
 */
export default function TransactionForm({
  accounts,
  categories,
  defaultValues,
  submitLabel,
  onSubmit,
  onCancel,
}: TransactionFormProps): React.JSX.Element {
  const {
    register,
    handleSubmit,
    control,
    formState: { errors, isSubmitting },
  } = useForm<TransactionFormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      accountId: accounts[0]?.id ?? '',
      categoryId: '',
      amount: '',
      date: '',
      description: '',
      direction: 'out',
      ...defaultValues,
    },
  });

  const selectedCategoryId = useWatch({ control, name: 'categoryId' });
  const selectedKind = categories.find((category) => category.id === selectedCategoryId)?.kind;

  const submit = handleSubmit(async (values) => {
    const category = categories.find((candidate) => candidate.id === values.categoryId);
    const account = accounts.find((candidate) => candidate.id === values.accountId);
    const magnitude = parseAmount(values.amount);

    await onSubmit({
      accountId: values.accountId,
      categoryId: values.categoryId,
      // The whole point of the unsigned field: direction is the category's to decide,
      // and only a Transfer hands that decision back to the user.
      amount: isNegative(category, values.direction) ? -magnitude : magnitude,
      currency: account?.currency ?? 'BRL',
      date: values.date,
      description: values.description.trim(),
    });
  });

  return (
    <form onSubmit={submit} className="grid gap-4 sm:grid-cols-2" noValidate>
      <Field name="account" label="Conta" error={errors.accountId?.message}>
        {(id) => (
          <select id={id} className={cn(selectClasses)} {...register('accountId')}>
            <option value="">Escolha uma conta</option>
            {accounts.map((account) => (
              <option key={account.id} value={account.id}>
                {account.name}
              </option>
            ))}
          </select>
        )}
      </Field>

      <Field name="category" label="Categoria" error={errors.categoryId?.message}>
        {(id) => (
          <select id={id} className={cn(selectClasses)} {...register('categoryId')}>
            <option value="">Escolha uma categoria</option>
            {categoryKinds.map((kind) => (
              <optgroup key={kind} label={categoryKindLabels[kind]}>
                {categories.filter((category) => category.kind === kind).map((category) => (
                  <option key={category.id} value={category.id}>
                    {categoryLabel(category, categories)}
                  </option>
                ))}
              </optgroup>
            ))}
          </select>
        )}
      </Field>

      {selectedKind === 'Transfer' && (
        <fieldset className="grid gap-2 sm:col-span-2">
          <legend className="mb-2 text-sm leading-none font-medium">Direção</legend>
          <div className="flex gap-4 text-sm">
            <label className="flex items-center gap-2">
              <input type="radio" value="out" {...register('direction')} />
              Saída
            </label>
            <label className="flex items-center gap-2">
              <input type="radio" value="in" {...register('direction')} />
              Entrada
            </label>
          </div>
        </fieldset>
      )}

      <Field name="amount" label="Valor" error={errors.amount?.message}>
        {(id) => (
          <Input
            id={id}
            inputMode="decimal"
            placeholder="0,00"
            className="amount"
            {...register('amount')}
          />
        )}
      </Field>

      <Field name="date" label="Data" error={errors.date?.message}>
        {(id) => <Input id={id} type="date" {...register('date')} />}
      </Field>

      <div className="sm:col-span-2">
        <Field name="description" label="Descrição" error={errors.description?.message}>
          {(id) => <Input id={id} {...register('description')} />}
        </Field>
      </div>

      <div className="flex gap-2 sm:col-span-2">
        <Button type="submit" disabled={isSubmitting}>
          {submitLabel}
        </Button>

        {onCancel && (
          <Button type="button" variant="outline" onClick={onCancel}>
            Cancelar
          </Button>
        )}
      </div>
    </form>
  );
}

function isNegative(category: Category | undefined, direction: 'out' | 'in'): boolean {
  switch (category?.kind) {
    case 'Expense':
      return true;
    case 'Transfer':
      return direction === 'out';
    default:
      return false;
  }
}

/**
 * Label, control and error as one unit. The id is handed to the child rather than
 * guessed at, so the label is always bound to the control it names — which is also
 * what lets the tests find every field by its visible label.
 */
function Field({
  name,
  label,
  error,
  children,
}: {
  /** ASCII, and independent of the visible label: the label is Portuguese and
      accented, and an id is not the place for either. */
  name: string;
  label: string;
  error?: string | undefined;
  children: (id: string) => React.ReactNode;
}) {
  const id = `field-${name}`;

  return (
    <div className="grid gap-2">
      <Label htmlFor={id}>{label}</Label>
      {children(id)}
      {error && (
        <p role="alert" className="text-destructive text-sm">
          {error}
        </p>
      )}
    </div>
  );
}
