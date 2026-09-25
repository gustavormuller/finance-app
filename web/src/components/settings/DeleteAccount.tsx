import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from '@tanstack/react-router';
import { useState } from 'react';

import { api, ApiError } from '@/api/finance';
import Alert from '@/components/Alert';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';

/**
 * "Excluir minha conta" (023, ADR-013), the danger zone at the bottom of `/settings`. The
 * account goes at once and for good, so the button waits for the signed-in e-mail, typed.
 */
export default function DeleteAccount({ email }: { email: string }) {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const [typed, setTyped] = useState('');

  const remove = useMutation({
    mutationFn: api.deleteMe,
    onSuccess: async () => {
      // Off the protected pages first. Cleared while they are still mounted, `me` would be
      // fetched again and the guard would send the browser to /login without the notice.
      await navigate({ to: '/login', search: { notice: 'deleted' } });
      queryClient.clear();
    },
  });

  const confirmed = email !== '' && typed.trim() === email;

  return (
    <section
      aria-labelledby="delete-account-heading"
      className="glass border-destructive/40 grid gap-4 rounded-2xl p-5 sm:p-6"
    >
      <h3 id="delete-account-heading" className="text-destructive font-sans text-sm font-semibold">
        Excluir minha conta
      </h3>

      {remove.isError && <Alert>{failure(remove.error)}</Alert>}

      <div className="grid gap-2 text-sm leading-relaxed">
        <p>
          Excluir a conta apaga para sempre tudo o que você guardou aqui: contas, lançamentos, importações,
          investimentos e análises de IA, além das suas categorias e modelos de importação. Não dá para desfazer.
        </p>
        <p className="text-muted-foreground">
          Você sai do app logo em seguida. Entrar de novo com o mesmo Google cria uma conta nova, vazia.
        </p>
      </div>

      <form
        className="grid gap-2"
        onSubmit={(event) => {
          event.preventDefault();
          if (confirmed) {
            remove.mutate();
          }
        }}
      >
        <Label htmlFor="delete-account-email">Digite seu e-mail para confirmar</Label>
        <p id="delete-account-hint" className="text-muted-foreground text-sm">
          Para confirmar, digite <strong className="text-foreground break-all">{email}</strong>.
        </p>
        <div className="flex flex-col gap-3 sm:flex-row">
          <Input
            id="delete-account-email"
            type="email"
            autoComplete="off"
            autoCapitalize="none"
            spellCheck={false}
            aria-describedby="delete-account-hint"
            value={typed}
            disabled={remove.isPending}
            onChange={(event) => setTyped(event.target.value)}
          />
          <Button type="submit" variant="destructive" disabled={!confirmed || remove.isPending}>
            {remove.isPending ? 'Excluindo…' : 'Excluir definitivamente'}
          </Button>
        </div>
      </form>
    </section>
  );
}

/**
 * The API's own sentence when it sent one. Its 401 and 403, and a failure the API did not
 * handle, arrive without one, and a lost response may even follow a commit: none of them
 * claims that nothing was deleted.
 */
function failure(error: Error): string {
  if (error instanceof ApiError && error.status === 401) {
    return 'Sua sessão terminou. Entre de novo para excluir a conta.';
  }

  if (error instanceof ApiError && error.status < 500 && error.status !== 403) {
    return error.message;
  }

  return 'Não foi possível excluir a conta. Tente de novo.';
}
