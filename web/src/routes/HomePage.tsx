import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Link, useNavigate } from '@tanstack/react-router';

import { useMe } from '../auth/useMe';
import { Button } from '@/components/ui/button';

export default function HomePage() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  // Already resolved and cached: RequireAuth does not render this page until it is.
  const { data: user } = useMe();

  const logout = useMutation({
    mutationFn: async () => {
      const response = await fetch('/api/auth/logout', {
        method: 'POST',
        credentials: 'same-origin',
      });

      // A 403 here means the Origin check refused the request, which is a bug in the
      // configured origin rather than something to report as a signed-out state.
      if (!response.ok) {
        throw new Error(`/api/auth/logout answered ${response.status}`);
      }
    },
    onSuccess: async () => {
      // Invalidated before navigating, so the guard on the next protected visit asks
      // the API again instead of trusting a cache that says we are signed in.
      await queryClient.invalidateQueries({ queryKey: ['me'] });
      await navigate({ to: '/login' });
    },
  });

  if (!user) {
    return null;
  }

  return (
    <section className="mx-auto max-w-5xl px-4 py-8">
      <h2 className="text-2xl font-semibold tracking-tight">Início</h2>

      <p className="text-muted-foreground mt-1 text-sm">
        Conectado como{' '}
        <strong data-testid="current-user" className="text-foreground font-medium">
          {user.displayName ?? user.email}
        </strong>
      </p>

      {/*
        Three doors rather than a dashboard. The dashboard is 005's, and an empty
        panel promising one would be worse than a plain list of where to go.
      */}
      <div className="mt-8 grid gap-3 sm:grid-cols-3">
        <Shortcut to="/transactions" title="Lançamentos" hint="Registrar e consultar entradas e saídas" />
        <Shortcut to="/accounts" title="Contas" hint="Bancos, cartões e dinheiro em espécie" />
        <Shortcut to="/categories" title="Categorias" hint="Como as despesas e receitas são agrupadas" />
      </div>

      <div className="mt-10">
        <Button
          type="button"
          variant="outline"
          onClick={() => logout.mutate()}
          disabled={logout.isPending}
        >
          Sair
        </Button>

        {logout.isError && (
          <p role="alert" className="text-destructive mt-2 text-sm">
            Não foi possível sair. Tente novamente.
          </p>
        )}
      </div>
    </section>
  );
}

function Shortcut({ to, title, hint }: { to: string; title: string; hint: string }) {
  return (
    <Link
      to={to}
      className="hover:bg-accent block rounded-lg border p-4 transition-colors"
    >
      <span className="font-medium">{title}</span>
      <span className="text-muted-foreground mt-1 block text-sm">{hint}</span>
    </Link>
  );
}
