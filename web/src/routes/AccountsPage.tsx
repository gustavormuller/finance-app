import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';

import { api, type Account, type AccountInput, type AccountType } from '@/api/finance';
import Alert from '@/components/Alert';
import { accountTypeLabels, accountTypes } from '@/lib/labels';
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
    <section className="mx-auto max-w-5xl px-4 py-8">
      <div className="mb-6 flex items-center justify-between gap-4">
        <h2 className="text-2xl font-semibold tracking-tight">Contas</h2>

        {!creating && !editing && <Button onClick={() => setCreating(true)}>Nova conta</Button>}
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
            <Label htmlFor="account-name">Nome</Label>
            <Input id="account-name" name="name" defaultValue={editing?.name ?? ''} required />
          </div>

          <div className="grid gap-2">
            <Label htmlFor="account-type">Tipo</Label>
            <select
              id="account-type"
              name="type"
              className={selectClasses}
              defaultValue={editing?.type ?? 'Checking'}
            >
              {accountTypes.map((type) => (
                <option key={type} value={type}>
                  {accountTypeLabels[type]}
                </option>
              ))}
            </select>
          </div>

          <div className="grid gap-2">
            <Label htmlFor="account-currency">Moeda</Label>
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
              {editing ? 'Salvar conta' : 'Criar conta'}
            </Button>
            <Button type="button" variant="outline" onClick={close}>
              Cancelar
            </Button>
          </div>
        </form>
      )}

      {failure && <Alert>{failure}</Alert>}

      {accounts.data?.length === 0 ? (
        <p className="text-muted-foreground border-border border-t py-16 text-center text-sm">
          Nenhuma conta ainda. Cadastre a primeira para começar a registrar lançamentos.
        </p>
      ) : (
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Nome</TableHead>
              <TableHead>Tipo</TableHead>
              {/* Only BRL exists in practice, so it is the first thing a phone can
                  afford to drop. */}
              <TableHead className="hidden sm:table-cell">Moeda</TableHead>
              <TableHead className="w-[140px]" />
            </TableRow>
          </TableHeader>
          <TableBody>
            {(accounts.data ?? []).map((account) => (
              <TableRow key={account.id}>
                <TableCell className="font-medium">{account.name}</TableCell>
                <TableCell>{accountTypeLabels[account.type]}</TableCell>
                <TableCell className="hidden tabular-nums sm:table-cell">{account.currency}</TableCell>
                <TableCell>
                  <div className="flex flex-wrap justify-end gap-1">
                    <Button variant="ghost" size="sm" onClick={() => setEditing(account)}>
                      Editar
                    </Button>
                    <Button variant="ghost" size="sm" onClick={() => remove.mutate(account.id)}>
                      Excluir
                    </Button>
                  </div>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
    </section>
  );
}
