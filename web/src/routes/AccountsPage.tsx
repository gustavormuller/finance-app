import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';

import { api, type Account, type AccountInput, type AccountType } from '@/api/finance';
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

const TYPES: AccountType[] = ['Checking', 'Savings', 'CreditCard', 'Cash', 'Investment'];

const selectClasses =
  'border-input dark:bg-input/30 h-9 w-full rounded-md border bg-transparent px-3 py-1 ' +
  'text-base shadow-xs outline-none md:text-sm';

export default function AccountsPage() {
  const queryClient = useQueryClient();
  const [editing, setEditing] = useState<Account | null>(null);
  const [creating, setCreating] = useState(false);
  const [failure, setFailure] = useState<string | null>(null);

  const accounts = useQuery({ queryKey: ['accounts'], queryFn: api.listAccounts });

  const close = () => {
    setCreating(false);
    setEditing(null);
    setFailure(null);
  };

  const save = useMutation({
    mutationFn: (input: AccountInput) =>
      editing ? api.updateAccount(editing.id, input) : api.createAccount(input),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['accounts'] });
      close();
    },
    onError: (error: Error) => setFailure(error.message),
  });

  const remove = useMutation({
    mutationFn: (id: string) => api.deleteAccount(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['accounts'] }),
    // The 409 body carries a sentence saying how many transactions are in the way.
    // Showing it is the whole reason the endpoint bothers to compose one.
    onError: (error: Error) => setFailure(error.message),
  });

  return (
    <section className="mx-auto max-w-3xl px-4 py-8">
      <div className="mb-6 flex items-center justify-between gap-4">
        <h2 className="text-2xl font-semibold tracking-tight">Accounts</h2>

        {!creating && !editing && <Button onClick={() => setCreating(true)}>New account</Button>}
      </div>

      {(creating || editing) && (
        <form
          className="border-border mb-8 grid gap-4 border-b pb-8 sm:grid-cols-3"
          onSubmit={(event) => {
            event.preventDefault();
            const form = new FormData(event.currentTarget);

            save.mutate({
              name: String(form.get('name') ?? ''),
              type: String(form.get('type') ?? 'Checking') as AccountType,
              currency: String(form.get('currency') ?? 'BRL').toUpperCase(),
            });
          }}
        >
          <div className="grid gap-2">
            <Label htmlFor="account-name">Name</Label>
            <Input id="account-name" name="name" defaultValue={editing?.name ?? ''} required />
          </div>

          <div className="grid gap-2">
            <Label htmlFor="account-type">Type</Label>
            <select
              id="account-type"
              name="type"
              className={selectClasses}
              defaultValue={editing?.type ?? 'Checking'}
            >
              {TYPES.map((type) => (
                <option key={type} value={type}>
                  {type}
                </option>
              ))}
            </select>
          </div>

          <div className="grid gap-2">
            <Label htmlFor="account-currency">Currency</Label>
            <Input
              id="account-currency"
              name="currency"
              maxLength={3}
              className="uppercase"
              defaultValue={editing?.currency ?? 'BRL'}
            />
          </div>

          <div className="flex gap-2 sm:col-span-3">
            <Button type="submit" disabled={save.isPending}>
              {editing ? 'Save account' : 'Create account'}
            </Button>
            <Button type="button" variant="outline" onClick={close}>
              Cancel
            </Button>
          </div>
        </form>
      )}

      {failure && (
        <p role="alert" className="text-destructive mb-4 text-sm">
          {failure}
        </p>
      )}

      {accounts.data?.length === 0 ? (
        <p className="text-muted-foreground border-border border-t py-16 text-center text-sm">
          No accounts yet. Add the first one to start recording transactions.
        </p>
      ) : (
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Name</TableHead>
              <TableHead>Type</TableHead>
              <TableHead>Currency</TableHead>
              <TableHead className="w-[140px]" />
            </TableRow>
          </TableHeader>
          <TableBody>
            {(accounts.data ?? []).map((account) => (
              <TableRow key={account.id}>
                <TableCell className="font-medium">{account.name}</TableCell>
                <TableCell>{account.type}</TableCell>
                <TableCell className="tabular-nums">{account.currency}</TableCell>
                <TableCell className="text-right">
                  <Button variant="ghost" size="sm" onClick={() => setEditing(account)}>
                    Edit
                  </Button>
                  <Button variant="ghost" size="sm" onClick={() => remove.mutate(account.id)}>
                    Delete
                  </Button>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
    </section>
  );
}
