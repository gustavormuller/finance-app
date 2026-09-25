import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, useSearch } from '@tanstack/react-router';
import { useState } from 'react';

import { api, type Transaction } from '@/api/finance';
import Amount from '@/components/Amount';
import Alert from '@/components/Alert';
import EmptyState from '@/components/EmptyState';
import { formatDate } from '@/lib/labels';
import TransactionForm from '@/components/TransactionForm';
import { selectClasses } from '@/components/FormField';
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

  // Arriving from an import's done step, or from an account (015): show that batch or
  // that account, whatever the dates, rather than the current month with most of it
  // filtered out.
  const { importBatchId, accountId } = useSearch({ strict: false }) as { importBatchId?: string; accountId?: string };
  const [filter, setFilter] = useState(
    importBatchId || accountId
      ? { from: '', to: '', accountId: accountId ?? '', categoryId: '', importBatchId: importBatchId ?? '' }
      : { ...currentMonth(), accountId: '', categoryId: '', importBatchId: '' },
  );
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
    filter.from !== '' ||
    filter.to !== '' ||
    filter.accountId !== '' ||
    filter.categoryId !== '' ||
    filter.importBatchId !== '';

  return (
    <section className="grid gap-6 [&>*]:min-w-0">
      <div className="flex flex-wrap items-center justify-between gap-4">
        <h2 className="text-3xl font-semibold tracking-tight">Lançamentos</h2>

        {!creating && !editing && (
          <Button onClick={() => setCreating(true)}>Novo lançamento</Button>
        )}
      </div>

      {(creating || editing) && (
        <div className="glass rounded-2xl p-5 sm:p-6">
          <TransactionForm
            accounts={accounts.data ?? []}
            categories={categories.data ?? []}
            submitLabel={editing ? 'Salvar lançamento' : 'Criar lançamento'}
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
                    // Only read for a Transfer, whose sign the user chose.
                    direction: editing.amount < 0 ? 'out' : 'in',
                    date: editing.date,
                    description: editing.description,
                  },
                }
              : {})}
          />
        </div>
      )}

      <div className="glass grid grid-cols-2 gap-3 rounded-2xl p-4 sm:grid-cols-4">
        <div className="grid gap-2">
          <Label htmlFor="filter-from">De</Label>
          <Input
            id="filter-from"
            type="date"
            value={filter.from}
            onChange={(event) => page1({ from: event.target.value })}
          />
        </div>

        <div className="grid gap-2">
          <Label htmlFor="filter-to">Até</Label>
          <Input
            id="filter-to"
            type="date"
            value={filter.to}
            onChange={(event) => page1({ to: event.target.value })}
          />
        </div>

        <FilterSelect
          id="filter-account"
          label="Filtrar por conta"
          value={filter.accountId}
          onChange={(accountId) => page1({ accountId })}
          options={(accounts.data ?? []).map((account) => [account.id, account.name])}
          allLabel="Todas as contas"
        />

        <FilterSelect
          id="filter-category"
          label="Filtrar por categoria"
          value={filter.categoryId}
          onChange={(categoryId) => page1({ categoryId })}
          options={(categories.data ?? []).map((category) => [category.id, category.name])}
          allLabel="Todas as categorias"
        />
      </div>

      {filter.importBatchId !== '' && (
        <p className="bg-accent text-foreground flex items-center justify-between gap-4 rounded-xl px-4 py-2.5 text-sm">
          <span>Mostrando apenas os lançamentos de uma importação.</span>
          <Link to="/transactions" search={{}} className="underline" onClick={() => page1({ importBatchId: '' })}>
            Mostrar todos
          </Link>
        </p>
      )}

      {failure && <Alert>{failure}</Alert>}

      {items.length === 0 && !transactions.isLoading ? (
        <EmptyState filtered={isFiltered} />
      ) : (
        <Table>
          <TableHeader>
            <TableRow>
              {/* On a phone there is not room for five columns and the amount is the
                  one you came for, so date, category and account move under the
                  description instead of pushing the amount off the side. */}
              <TableHead className="hidden w-[110px] md:table-cell">Data</TableHead>
              <TableHead>Descrição</TableHead>
              <TableHead className="hidden md:table-cell">Categoria</TableHead>
              <TableHead className="hidden md:table-cell">Conta</TableHead>
              <TableHead className="text-right">Valor</TableHead>
              <TableHead className="w-auto md:w-[130px]" />
            </TableRow>
          </TableHeader>
          <TableBody>
            {items.map((transaction) => (
              <TableRow key={transaction.id}>
                <TableCell className="hidden tabular-nums md:table-cell">
                  {formatDate(transaction.date)}
                </TableCell>
                <TableCell className="font-medium">
                  {transaction.description}
                  <span className="text-muted-foreground block text-xs font-normal md:hidden">
                    <span className="tabular-nums">{formatDate(transaction.date)}</span> ·{' '}
                    {transaction.categoryName} · {transaction.accountName}
                  </span>
                </TableCell>
                <TableCell className="hidden md:table-cell">{transaction.categoryName}</TableCell>
                <TableCell className="hidden md:table-cell">{transaction.accountName}</TableCell>
                <TableCell className="text-right">
                  <Amount value={transaction.amount} />
                </TableCell>
                <TableCell>
                  <div className="flex justify-end gap-1">
                    <Button variant="ghost" size="sm" onClick={() => setEditing(transaction)}>
                      Editar
                    </Button>
                    <Button
                      variant="ghost"
                      size="sm"
                      onClick={() => remove.mutate(transaction.id)}
                    >
                      Excluir
                    </Button>
                  </div>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}

      {total > PAGE_SIZE && (
        <div className="mt-6 flex items-center justify-between">
          <p className="text-muted-foreground text-sm">
            {(page - 1) * PAGE_SIZE + 1}–{Math.min(page * PAGE_SIZE, total)} de {total}
          </p>

          <div className="flex gap-2">
            <Button
              variant="outline"
              size="sm"
              disabled={page === 1}
              onClick={() => setPage((current) => current - 1)}
            >
              Anterior
            </Button>
            <Button
              variant="outline"
              size="sm"
              disabled={page * PAGE_SIZE >= total}
              onClick={() => setPage((current) => current + 1)}
            >
              Próxima
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
        className={selectClasses}
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
