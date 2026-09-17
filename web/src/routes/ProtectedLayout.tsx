import { Link, Outlet } from '@tanstack/react-router';

import RequireAuth from './RequireAuth';

const LINKS: [string, string][] = [
  ['/', 'Início'],
  ['/transactions', 'Lançamentos'],
  ['/accounts', 'Contas'],
  ['/categories', 'Categorias'],
];

/**
 * The layout route every protected page hangs off. Protection lives in
 * {@link RequireAuth} so it can be tested without a page of its own.
 *
 * The navigation is styled as a second row of the shell's header — same border, same
 * container width — because it cannot live in App.tsx: it needs the router's context
 * for its links, and it must not be shown to someone who is not signed in.
 */
export default function ProtectedLayout() {
  return (
    <RequireAuth>
      <nav className="bg-card border-b">
        <div className="mx-auto flex max-w-5xl gap-1 overflow-x-auto px-4 py-2">
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
        </div>
      </nav>

      <Outlet />
    </RequireAuth>
  );
}
