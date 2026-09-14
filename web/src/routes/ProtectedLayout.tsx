import { Outlet } from '@tanstack/react-router';

import RequireAuth from './RequireAuth';

/**
 * The layout route every protected page hangs off. Protection lives in
 * {@link RequireAuth} so it can be tested without a page of its own.
 */
export default function ProtectedLayout() {
  return (
    <RequireAuth>
      <Outlet />
    </RequireAuth>
  );
}
