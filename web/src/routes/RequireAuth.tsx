import { useNavigate } from '@tanstack/react-router';
import { useEffect, type ReactNode } from 'react';

import { useMe } from '../auth/useMe';

/**
 * Renders its children only for a signed-in caller, and sends everyone else to
 * /login.
 */
export default function RequireAuth({ children }: { children: ReactNode }) {
  const navigate = useNavigate();
  const { data: user, isPending, isError } = useMe();

  // An error is treated as unauthenticated rather than shown: if the API cannot be
  // reached, nobody can be proven signed in, and the login page is the honest answer.
  const unauthenticated = !isPending && (isError || user === null);

  useEffect(() => {
    if (unauthenticated) {
      void navigate({ to: '/login', replace: true });
    }
  }, [unauthenticated, navigate]);

  // Nothing while the answer is unknown. Flashing the login page at someone who turns
  // out to be signed in is worse than a blank frame for one request.
  if (isPending || unauthenticated) {
    return null;
  }

  return <>{children}</>;
}
