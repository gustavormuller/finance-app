import { Link, Outlet } from '@tanstack/react-router';

import RequireAuth from './RequireAuth';

/**
 * The layout route every protected page hangs off. Protection lives in
 * {@link RequireAuth} so it can be tested without a page of its own.
 */
export default function ProtectedLayout() {
  return (
    <RequireAuth>
      <nav className="border-border mb-2 flex gap-1 border-b pb-2">
        {[
          ['/', 'Home'],
          ['/transactions', 'Transactions'],
          ['/accounts', 'Accounts'],
          ['/categories', 'Categories'],
        ].map(([to, label]) => (
          <Link
            key={to}
            to={to!}
            // activeProps rather than a manual pathname comparison, so the router
            // stays the one source of truth about where we are.
            activeProps={{ className: 'bg-secondary text-secondary-foreground' }}
            activeOptions={{ exact: to === '/' }}
            className="hover:bg-secondary/60 rounded-md px-3 py-1.5 text-sm font-medium"
          >
            {label}
          </Link>
        ))}
      </nav>

      <Outlet />
    </RequireAuth>
  );
}
