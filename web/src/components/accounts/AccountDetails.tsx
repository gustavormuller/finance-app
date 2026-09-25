import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from '@tanstack/react-router';
import { useState } from 'react';

import { api, type Account } from '@/api/finance';
import Alert from '@/components/Alert';
import { Button } from '@/components/ui/button';
import { accountRefusal, type Refusal } from '@/lib/accounts';

import AccountForm from './AccountForm';

/**
 * "Detalhes da conta" (015): the edit form and the delete, with the messages the
 * accounts page always had. A refused delete shows the 409's sentence, which says how
 * many transactions are in the way — the reason the endpoint composes one.
 */
export default function AccountDetails({ account }: { account: Account }): React.JSX.Element {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const [refused, setRefused] = useState<Refusal | null>(null);
  const [saved, setSaved] = useState(false);

  const refresh = () =>
    Promise.all([
      queryClient.invalidateQueries({ queryKey: ['accounts'] }),
      queryClient.invalidateQueries({ queryKey: ['dashboard'] }),
    ]);

  const start = () => {
    setRefused(null);
    setSaved(false);
  };

  const save = useMutation({
    mutationFn: (input: Parameters<typeof api.updateAccount>[1]) => api.updateAccount(account.id, input),
    onMutate: start,
    onSuccess: async () => {
      await refresh();
      setSaved(true);
    },
    onError: (error: Error) => setRefused(accountRefusal(error)),
  });

  const remove = useMutation({
    mutationFn: () => api.deleteAccount(account.id),
    onMutate: start,
    onSuccess: async () => {
      // Out of the cache before leaving, so `/accounts` cannot open the account just deleted.
      queryClient.setQueryData<Account[]>(['accounts'], (current) => current?.filter((each) => each.id !== account.id));
      await navigate({ to: '/accounts' });
      await refresh();
    },
    onError: (error: Error) => setRefused({ fields: {}, message: error.message }),
  });

  return (
    <div className="grid gap-4">
      {refused?.message && <Alert>{refused.message}</Alert>}

      <AccountForm
        account={account}
        submitLabel="Salvar conta"
        busy={save.isPending || remove.isPending}
        errors={refused?.fields ?? {}}
        onSubmit={(input) => save.mutate(input)}
      >
        <Button
          type="button"
          variant="outline"
          className="text-destructive sm:ml-auto"
          disabled={save.isPending || remove.isPending}
          onClick={() => remove.mutate()}
        >
          Excluir conta
        </Button>
      </AccountForm>

      {saved && (
        <p role="status" className="text-muted-foreground text-sm">
          Conta salva.
        </p>
      )}
    </div>
  );
}
