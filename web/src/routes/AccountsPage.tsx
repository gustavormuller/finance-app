import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';

import { api, ApiError, type Account, type AccountInput, type AccountType } from '@/api/finance';
import Alert from '@/components/Alert';
import Amount from '@/components/Amount';
import { accountTypeLabels, accountTypes } from '@/lib/labels';
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


/** The fields this form renders, so a 400 naming one is shown under it. */
const FIELDS = ['name', 'type', 'currency', 'openingBalance'] as const;

/**
 * An opening balance as typed. pt-BR first — `-1.234,56` — because that is how the
 * app shows money; a plain `1234.56` still works when there is no comma. The sign is
 * kept (a credit card starts negative), including the U+2212 minus `Amount` prints.
 * Empty means zero. NaN means "not a number", for the caller to refuse.
 */
function parseOpeningBalance(typed: string): number {
  const text = typed.trim().replace('−', '-');

  if (text === '') {
    return 0;
  }

  return Number(text.includes(',') ? text.replaceAll('.', '').replace(',', '.') : text);
}

/** Shown back the way it is typed: decimal comma, no grouping. */
function typedOpeningBalance(value: number): string {
  return value.toFixed(2).replace('.', ',');
}

export default function AccountsPage() {
  const queryClient = useQueryClient();
  const [editing, setEditing] = useState<Account | null>(null);
  const [creating, setCreating] = useState(false);
  const [failure, setFailure] = useState<string | null>(null);
  const [fieldErrors, setFieldErrors] = useState<Record<string, string[]>>({});

  const accounts = useQuery({ queryKey: ['accounts'], queryFn: api.listAccounts });

  const close = () => {
    setCreating(false);
    setEditing(null);
    setFailure(null);
    setFieldErrors({});
  };

  // A 400's messages go under the fields they name; anything else is one sentence
  // above the list, never the English default title of a validation problem.
  const refuse = (error: Error) => {
    const fields = error instanceof ApiError ? error.fields : {};
    const elsewhere = Object.entries(fields)
      .filter(([field]) => !(FIELDS as readonly string[]).includes(field))
      .flatMap(([, messages]) => messages);

    setFieldErrors(fields);
    setFailure(
      Object.keys(fields).length === 0 ? error.message : elsewhere.length > 0 ? elsewhere.join(' ') : null,
    );
  };

  const save = useMutation({
    mutationFn: (input: AccountInput) =>
      editing ? api.updateAccount(editing.id, input) : api.createAccount(input),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['accounts'] });
      close();
    },
    onError: refuse,
  });

  const remove = useMutation({
    mutationFn: (id: string) => api.deleteAccount(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['accounts'] }),
    // The 409 body carries a sentence saying how many transactions are in the way.
    // Showing it is the whole reason the endpoint bothers to compose one.
    onError: (error: Error) => setFailure(error.message),
  });

  return (
    <section className="grid gap-6">
      <div className="flex flex-wrap items-center justify-between gap-4">
        <h2 className="text-3xl font-semibold tracking-tight">Contas</h2>

        {!creating && !editing && <Button onClick={() => setCreating(true)}>Nova conta</Button>}
      </div>

      {(creating || editing) && (
        <form
          className="glass grid gap-4 rounded-2xl p-5 sm:grid-cols-4 sm:p-6"
          onSubmit={(event) => {
            event.preventDefault();
            const form = new FormData(event.currentTarget);
            const openingBalance = parseOpeningBalance(String(form.get('openingBalance') ?? ''));

            if (!Number.isFinite(openingBalance)) {
              setFieldErrors({ openingBalance: ['Informe um número.'] });
              return;
            }

            save.mutate({
              name: String(form.get('name') ?? ''),
              type: String(form.get('type') ?? 'Checking') as AccountType,
              currency: String(form.get('currency') ?? 'BRL').toUpperCase(),
              openingBalance,
            });
          }}
        >
          <div className="grid gap-2">
            <Label htmlFor="account-name">Nome</Label>
            <Input id="account-name" name="name" defaultValue={editing?.name ?? ''} required />
            <FieldError messages={fieldErrors.name} />
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
            <FieldError messages={fieldErrors.type} />
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
            <FieldError messages={fieldErrors.currency} />
          </div>

          <div className="grid gap-2">
            <Label htmlFor="account-opening-balance">Saldo inicial</Label>
            <Input
              id="account-opening-balance"
              name="openingBalance"
              inputMode="decimal"
              placeholder="0,00"
              className="amount tabular-nums"
              defaultValue={editing ? typedOpeningBalance(editing.openingBalance) : ''}
            />
            <FieldError messages={fieldErrors.openingBalance} />
          </div>

          <div className="flex gap-2 sm:col-span-4">
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
        <p className="text-muted-foreground glass rounded-2xl py-16 text-center text-sm">
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
              <TableHead className="hidden text-right sm:table-cell">Saldo inicial</TableHead>
              <TableHead className="w-[140px]" />
            </TableRow>
          </TableHeader>
          <TableBody>
            {(accounts.data ?? []).map((account) => (
              <TableRow key={account.id}>
                <TableCell className="font-medium">{account.name}</TableCell>
                <TableCell>{accountTypeLabels[account.type]}</TableCell>
                <TableCell className="hidden tabular-nums sm:table-cell">{account.currency}</TableCell>
                <TableCell className="hidden text-right sm:table-cell">
                  <Amount value={account.openingBalance} />
                </TableCell>
                <TableCell>
                  <div className="flex justify-end gap-1">
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

function FieldError({ messages }: { messages?: string[] | undefined }) {
  return messages && messages.length > 0 ? (
    <p role="alert" className="text-destructive text-sm">
      {messages.join(' ')}
    </p>
  ) : null;
}
