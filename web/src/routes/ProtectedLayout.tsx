import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Link, Outlet, useNavigate } from '@tanstack/react-router';

import { useMe } from '../auth/useMe';
import { Button } from '@/components/ui/button';
import RequireAuth from './RequireAuth';

const LINKS: [string, string][] = [
  ['/', 'Início'],
  ['/transactions', 'Lançamentos'],
  ['/import', 'Importar'],
  ['/investments', 'Investimentos'],
  ['/accounts', 'Contas'],
  ['/categories', 'Categorias'],
  ['/settings', 'Configurações'],
];

/**
 * The layout route every protected page hangs off. Protection lives in
 * {@link RequireAuth} so it can be tested without a page of its own.
 *
 * The navigation is styled as a second row of the shell's header — same border, same
 * container width — because it cannot live in App.tsx: it needs the router's context
 * for its links, and it must not be shown to someone who is not signed in.
 *
 * Who is signed in, and the way out, sit at the end of that row. They lived on the
 * old landing page until 005 made `/` the dashboard.
 */
export default function ProtectedLayout() {
  return (
    <RequireAuth>
      <nav className="bg-card border-b">
        <div className="mx-auto flex max-w-5xl items-center gap-1 overflow-x-auto px-4 py-2">
          {LINKS.map(([to, label]) => (
            <Link
              key={to}
              to={to}
              // activeProps rather than a manual pathname comparison, so the router
              // stays the one source of truth about where we are.
              activeProps={{ className: 'bg-secondary text-secondary-foreground' }}
              activeOptions={{ exact: to === '/' }}
              className="hover:bg-secondary/60 rounded-md px-3 py-1.5 text-sm font-medium whitespace-nowrap transition-colors"
            >
              {label}
            </Link>
          ))}

          <SignedIn />
        </div>
      </nav>

      <Outlet />
    </RequireAuth>
  );
}

function SignedIn() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  // Already resolved and cached: RequireAuth does not render its children until it is.
  const { data: user } = useMe();

  const logout = useMutation({
    mutationFn: async () => {
      const response = await fetch('/api/auth/logout', { method: 'POST', credentials: 'same-origin' });

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

  return (
    <div className="ml-auto flex items-center gap-2 pl-4 text-sm whitespace-nowrap">
      <strong data-testid="current-user" className="text-muted-foreground hidden font-medium sm:inline">
        {user?.displayName ?? user?.email}
      </strong>
      <Button type="button" variant="outline" size="sm" onClick={() => logout.mutate()} disabled={logout.isPending}>
        Sair
      </Button>
      {logout.isError && (
        <span role="alert" className="text-destructive">
          Não foi possível sair.
        </span>
      )}
    </div>
  );
}
