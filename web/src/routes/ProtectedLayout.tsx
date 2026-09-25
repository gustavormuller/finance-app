import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Link, Outlet, useNavigate } from '@tanstack/react-router';
import { House, Landmark, ListOrdered, Settings, Tags, TrendingUp, Upload, type LucideIcon } from 'lucide-react';

import { useMe } from '../auth/useMe';
import { Button } from '@/components/ui/button';
import RequireAuth from './RequireAuth';

const LINKS: [string, string, LucideIcon][] = [
  ['/', 'Início', House],
  ['/transactions', 'Lançamentos', ListOrdered],
  ['/import', 'Importar', Upload],
  ['/investments', 'Investimentos', TrendingUp],
  ['/accounts', 'Contas', Landmark],
  ['/categories', 'Categorias', Tags],
  ['/settings', 'Configurações', Settings],
];

/**
 * The layout route every protected page hangs off. Protection lives in
 * {@link RequireAuth} so it can be tested without a page of its own.
 *
 * 012: a glass sidebar beside the page on a wide screen, a scrolling strip above it on
 * a narrow one. Who is signed in, and the way out, close the navigation.
 */
export default function ProtectedLayout() {
  return (
    <RequireAuth>
      <div className="px-4 sm:px-6 lg:grid lg:grid-cols-[15rem_minmax(0,1fr)] lg:items-start lg:gap-8 lg:px-8">
        <nav
          aria-label="Principal"
          className="glass mb-6 flex items-center gap-1 overflow-x-auto rounded-2xl p-2 [scrollbar-width:none] lg:sticky lg:top-6 lg:mb-0 lg:flex-col lg:items-stretch lg:overflow-visible lg:p-3"
        >
          {LINKS.map(([to, label, Icon]) => (
            <Link
              key={to}
              to={to}
              // The router marks the active link with data-status, so it stays the
              // one source of truth about where we are.
              activeOptions={{ exact: to === '/' }}
              className="text-muted-foreground hover:bg-secondary hover:text-foreground data-[status=active]:bg-accent data-[status=active]:text-foreground data-[status=active]:[&>svg]:text-primary flex items-center gap-3 rounded-xl px-3 py-2.5 text-sm font-semibold whitespace-nowrap transition-colors"
            >
              <Icon className="size-[1.125rem] shrink-0" aria-hidden="true" />
              {label}
            </Link>
          ))}

          <SignedIn />
        </nav>

        <div className="min-w-0 pb-10">
          <Outlet />
        </div>
      </div>
    </RequireAuth>
  );
}

function SignedIn() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  // Already resolved and cached: RequireAuth does not render its children until it is.
  const { data: user } = useMe();
  const name = user?.displayName ?? user?.email ?? '';

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
    <div className="ml-auto flex items-center gap-3 pl-4 text-sm whitespace-nowrap lg:mt-4 lg:ml-0 lg:flex-wrap lg:border-t lg:px-2 lg:pt-4 lg:[&>button]:w-full">
      <span
        aria-hidden="true"
        className="bg-primary text-primary-foreground hidden size-8 shrink-0 items-center justify-center rounded-full text-xs font-semibold lg:flex"
      >
        {initials(name)}
      </span>
      <strong data-testid="current-user" className="hidden min-w-0 truncate font-semibold sm:inline lg:flex-1">
        {name}
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

/** "Gustavo Müller" → "GM"; an e-mail gives its first letter. */
function initials(name: string): string {
  const words = name.split(/[\s@.]+/).filter(Boolean);

  return ((words[0]?.[0] ?? '') + (words.length > 1 && !name.includes('@') ? words[words.length - 1]![0] : '')).toUpperCase();
}
