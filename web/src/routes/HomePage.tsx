import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from '@tanstack/react-router';

import { useMe } from '../auth/useMe';

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
    <section>
      <p>
        Signed in as <strong data-testid="current-user">{user.displayName ?? user.email}</strong>
      </p>

      <button type="button" onClick={() => logout.mutate()} disabled={logout.isPending}>
        Log out
      </button>

      {logout.isError && <p role="alert">Could not sign out. Try again.</p>}
    </section>
  );
}
