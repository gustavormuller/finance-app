import type { Account, Category, TransactionInput } from '@/api/finance';

export interface TransactionFormValues {
  accountId: string;
  categoryId: string;
  /** Unsigned, as typed. The sign comes from the category's kind, never the keyboard. */
  amount: string;
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
 * Create and edit, one form.
 *
 * The amount field takes an unsigned number and the sign is derived from the selected
 * category's kind, so "Salary: −3000" is not a mistake the interface lets you make.
 * The API enforces the same rule; this keeps the user from ever meeting it.
 */
export default function TransactionForm(props: TransactionFormProps): React.JSX.Element {
  throw new Error(`TransactionForm is not implemented (${props.submitLabel})`);
}
