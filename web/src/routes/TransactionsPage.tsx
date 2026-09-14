import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';

import { api, type Transaction } from '@/api/finance';
import Amount from '@/components/Amount';
import EmptyState from '@/components/EmptyState';
import TransactionForm from '@/components/TransactionForm';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';

const PAGE_SIZE = 50;

/** The current month, which is what the spec asks the filter to open on. */
function currentMonth() {
  const now = new Date();
  const first = new Date(now.getFullYear(), now.getMonth(), 1);
  const last = new Date(now.getFullYear(), now.getMonth() + 1, 0);

  return { from: isoDay(first), to: isoDay(last) };
}

/** Local calendar day, not UTC: toISOString() would shift the date west of UTC. */
function isoDay(date: Date) {
  return [
    date.getFullYear(),
    String(date.getMonth() + 1).padStart(2, '0'),
    String(date.getDate()).padStart(2, '0'),
  ].join('-');
}

export default function TransactionsPage() {
  const queryClient = useQueryClient();
  const [filter, setFilter] = useState({ ...currentMonth(), accountId: '', categoryId: '' });
  const [page, setPage] = useState(1);
  const [editing, setEditing] = useState<Transaction | null>(null);
  const [creating, setCreating] = useState(false);
  const [failure, setFailure] = useState<string | null>(null);

  const accounts = useQuery({ queryKey: ['accounts'], queryFn: api.listAccounts });
  const categories = useQuery({ queryKey: ['categories'], queryFn: api.listCategories });

  const query = { ...filter, page, pageSize: PAGE_SIZE };
  const transactions = useQuery({
    queryKey: ['transactions', query],
    queryFn: () => api.listTransactions(query),
  });

  const close = () => {
    setCreating(false);
    setEditing(null);
    setFailure(null);
  };

  const save = useMutation({
    mutationFn: (input: Parameters<typeof api.createTransaction>[0]) =>
      editing ? api.updateTransaction(editing.id, input) : api.createTransaction(input),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['transactions'] });
      close();
    },
    onError: (error: Error) => setFailure(error.message),
  });

  const remove = useMutation({
    mutationFn: (id: string) => api.deleteTransaction(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['transactions'] }),
    onError: (error: Error) => setFailure(error.message),
  });

  const page1 = (change: Partial<typeof filter>) => {
    // Any change to the filter invalidates the page number: page 3 of the old result
    // is not page 3 of the new one, and landing on an empty page reads as a bug.
    setFilter((current) => ({ ...current, ...change }));
    setPage(1);
  };

  const items = transactions.data?.items ?? [];
  const total = transactions.data?.total ?? 0;
  const isFiltered =
    filter.from !== '' || filter.to !== '' || filter.accountId !== '' || filter.categoryId !== '';

  return (
    <section className="mx-auto max-w-5xl px-4 py-8">
      <div className="mb-6 flex items-center justify-between gap-4">
        <h2 className="text-2xl font-semibold tracking-tight">Transactions</h2>

        {!creating && !editing && (
          <Button onClick={() => setCreating(true)}>New transaction</Button>
        )}
      </div>

      {(creating || editing) && (
        <div className="border-border mb-8 border-b pb-8">
          <TransactionForm
            accounts={accounts.data ?? []}
            categories={categories.data ?? []}
            submitLabel={editing ? 'Save transaction' : 'Create transaction'}
            onCancel={close}
            onSubmit={async (input) => {
              await save.mutateAsync(input);
            }}
            {...(editing
              ? {
                  defaultValues: {
                    accountId: editing.accountId,
                    categoryId: editing.categoryId,
                    // Shown unsigned: the field never carries a sign, in either
                    // direction.
                    amount: Math.abs(editing.amount).toFixed(2),
                    date: editing.date,
                    description: editing.description,
                  },
                }
              : {})}
          />
        </div>
      )}

      <div className="mb-6 grid gap-3 sm:grid-cols-4">
        <div className="grid gap-2">
          <Label htmlFor="filter-from">From</Label>
          <Input
            id="filter-from"
            type="date"
            value={filter.from}
            onChange={(event) => page1({ from: event.target.value })}
          />
        </div>

        <div className="grid gap-2">
          <Label htmlFor="filter-to">To</Label>
          <Input
            id="filter-to"
            type="date"
            value={filter.to}
            onChange={(event) => page1({ to: event.target.value })}
          />
        </div>

        <FilterSelect
          id="filter-account"
          label="Filter by account"
          value={filter.accountId}
          onChange={(accountId) => page1({ accountId })}
          options={(accounts.data ?? []).map((account) => [account.id, account.name])}
          allLabel="All accounts"
        />

        <FilterSelect
          id="filter-category"
          label="Filter by category"
          value={filter.categoryId}
          onChange={(categoryId) => page1({ categoryId })}
          options={(categories.data ?? []).map((category) => [category.id, category.name])}
          allLabel="All categories"
        />
      </div>

      {failure && (
        <p role="alert" className="text-destructive mb-4 text-sm">
          {failure}
        </p>
      )}

      {items.length === 0 && !transactions.isLoading ? (
        <EmptyState filtered={isFiltered} />
      ) : (
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead className="w-[110px]">Date</TableHead>
              <TableHead>Description</TableHead>
              <TableHead>Category</TableHead>
              <TableHead>Account</TableHead>
              <TableHead className="text-right">Amount</TableHead>
              <TableHead className="w-[130px]" />
            </TableRow>
          </TableHeader>
          <TableBody>
            {items.map((transaction) => (
              <TableRow key={transaction.id}>
                <TableCell className="tabular-nums">{transaction.date}</TableCell>
                <TableCell className="font-medium">{transaction.description}</TableCell>
                <TableCell>{transaction.categoryName}</TableCell>
                <TableCell>{transaction.accountName}</TableCell>
                <TableCell className="text-right">
                  <Amount value={transaction.amount} />
                </TableCell>
                <TableCell className="text-right">
                  <Button variant="ghost" size="sm" onClick={() => setEditing(transaction)}>
                    Edit
                  </Button>
                  <Button
                    variant="ghost"
                    size="sm"
                    onClick={() => remove.mutate(transaction.id)}
                  >
                    Delete
                  </Button>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}

      {total > PAGE_SIZE && (
        <div className="mt-6 flex items-center justify-between">
          <p className="text-muted-foreground text-sm">
            {(page - 1) * PAGE_SIZE + 1}–{Math.min(page * PAGE_SIZE, total)} of {total}
          </p>

          <div className="flex gap-2">
            <Button
              variant="outline"
              size="sm"
              disabled={page === 1}
              onClick={() => setPage((current) => current - 1)}
            >
              Previous
            </Button>
            <Button
              variant="outline"
              size="sm"
              disabled={page * PAGE_SIZE >= total}
              onClick={() => setPage((current) => current + 1)}
            >
              Next
            </Button>
          </div>
        </div>
      )}
    </section>
  );
}

function FilterSelect({
  id,
  label,
  value,
  onChange,
  options,
  allLabel,
}: {
  id: string;
  label: string;
  value: string;
  onChange: (value: string) => void;
  options: [string, string][];
  allLabel: string;
}) {
  return (
    <div className="grid gap-2">
      <Label htmlFor={id}>{label}</Label>
      <select
        id={id}
        value={value}
        onChange={(event) => onChange(event.target.value)}
        className="border-input dark:bg-input/30 h-9 w-full rounded-md border bg-transparent px-3 py-1 text-base shadow-xs outline-none md:text-sm"
      >
        <option value="">{allLabel}</option>
        {options.map(([optionValue, optionLabel]) => (
          <option key={optionValue} value={optionValue}>
            {optionLabel}
          </option>
        ))}
      </select>
    </div>
  );
}
