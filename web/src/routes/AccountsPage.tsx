import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Navigate, Outlet, useNavigate, useParams, useSearch } from '@tanstack/react-router';
import { Plus } from 'lucide-react';
import { useState } from 'react';

import { api, type AccountInput } from '@/api/finance';
import AccountForm from '@/components/accounts/AccountForm';
import AccountList from '@/components/accounts/AccountList';
import { useAccounts, useBalances, useImports } from '@/components/accounts/queries';
import Alert from '@/components/Alert';
import Card from '@/components/Card';
import PageHeader from '@/components/PageHeader';
import { Button } from '@/components/ui/button';
import { accountRefusal } from '@/lib/accounts';
import { useOpenForm, type Opening } from '@/lib/openForm';
import type { Refusal } from '@/lib/refusal';

/**
 * `/accounts`: the accounts on the left, the selected one on the right (the
 * outlet, `/accounts/$accountId`). Creating an account happens here, above both, and
 * selects the account it created.
 */
export default function AccountsPage(): React.JSX.Element {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const { accountId } = useParams({ strict: false });
  const { tab } = useSearch({ from: '/protected/accounts' });
  // The new-account form, which edits no account: only its openings tell two apart.
  const forms = useOpenForm<null>();
  const opening = forms.open;
  const [refused, setRefused] = useState<Refusal | null>(null);

  const accounts = useAccounts();
  const summary = useBalances();
  const imports = useImports();

  const close = () => {
    forms.close();
    setRefused(null);
  };

  const create = useMutation({
    mutationFn: ({ input }: { input: AccountInput; from: Opening<null> }) => api.createAccount(input),
    onMutate: () => setRefused(null),
    onSuccess: async (created, { from }) => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['accounts'] }),
        queryClient.invalidateQueries({ queryKey: ['dashboard'] }),
      ]);
      // Its form cancelled, or another opened since: the new account is in the list, and the
      // page stays as the person left it.
      if (forms.current() !== from) {
        return;
      }
      // Selected before the form closes: closing brings the outlet back, and on `/accounts`
      // alone that is FirstAccount, whose redirect to the first account would win.
      await navigate({ to: '/accounts/$accountId', params: { accountId: created.id } });
      close();
    },
    onError: (error: Error) => setRefused(accountRefusal(error)),
  });

  return (
    <section className="grid gap-6 [&>*]:min-w-0">
      <PageHeader
        title="Contas"
        subtitle="O extrato entra pela conta a que ele pertence: sem escolher conta de novo, com o histórico dela ao lado."
        actions={
          !opening && (
            <Button onClick={() => forms.show(null)}>
              <Plus aria-hidden="true" />
              Nova conta
            </Button>
          )
        }
      />

      {opening && (
        <Card aria-labelledby="new-account-heading" className="grid gap-4">
          <h3 id="new-account-heading" className="text-lg font-semibold">
            Nova conta
          </h3>
          {refused?.message && <Alert>{refused.message}</Alert>}
          <AccountForm
            submitLabel="Criar conta"
            busy={create.isPending}
            errors={refused?.fields ?? {}}
            onSubmit={(input) => create.mutate({ input, from: opening })}
          >
            <Button type="button" variant="outline" onClick={close}>
              Cancelar
            </Button>
          </AccountForm>
        </Card>
      )}

      {accounts.isError && <Alert>Não foi possível carregar as contas. Recarregue a página para tentar de novo.</Alert>}

      {accounts.data?.length === 0 && !opening && (
        <Card as="div" className="text-muted-foreground py-16 text-center">
          <p className="text-foreground text-sm font-medium">Nenhuma conta ainda</p>
          <p className="mt-1 text-sm">
            Cadastre a primeira para começar a registrar lançamentos e importar extratos.
          </p>
          <Button className="mt-4" onClick={() => forms.show(null)}>
            Criar a primeira conta
          </Button>
        </Card>
      )}

      {accounts.data && accounts.data.length > 0 && (
        // While the import maps columns or reviews rows (its step says `data-wide`), the
        // list steps aside and the detail takes the width: those tables are the work.
        <div className="group/accounts grid gap-6 xl:grid-cols-[22rem_minmax(0,1fr)] xl:items-start xl:has-[[data-wide]]:grid-cols-1 [&>*]:min-w-0">
          <div className="group-has-[[data-wide]]/accounts:hidden">
            <AccountList
              accounts={accounts.data}
              summary={summary.data}
              batches={imports.data ?? []}
              selectedId={accountId}
              tab={tab}
            />
          </div>
          {/* Hidden while creating, so the page never shows two account forms at once. */}
          {!opening && <Outlet />}
        </div>
      )}
    </section>
  );
}

/** `/accounts` alone: the first account, in the tab asked for. */
export function FirstAccount(): React.JSX.Element | null {
  const { tab } = useSearch({ from: '/protected/accounts/' });
  const first = useAccounts().data?.[0];

  return first ? (
    <Navigate to="/accounts/$accountId" params={{ accountId: first.id }} search={tab ? { tab } : {}} replace />
  ) : null;
}
