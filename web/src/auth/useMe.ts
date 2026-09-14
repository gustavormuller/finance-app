import { useQuery } from '@tanstack/react-query';

export type AuthenticatedUser = {
  id: string;
  email: string;
  displayName: string | null;
  aiEnabled: boolean;
};

/**
 * Resolves to null when nobody is signed in. A 401 from this endpoint is the normal
 * answer for a visitor, not a failure, so it must not surface as a query error.
 */
async function fetchMe(): Promise<AuthenticatedUser | null> {
  const response = await fetch('/api/auth/me', { credentials: 'same-origin' });

  if (response.status === 401) {
    return null;
  }

  if (!response.ok) {
    throw new Error(`/api/auth/me answered ${response.status}`);
  }

  return (await response.json()) as AuthenticatedUser;
}

export function useMe() {
  return useQuery({
    queryKey: ['me'],
    queryFn: fetchMe,
    // A retried 401 would keep the guard in its loading state, showing a blank page
    // to someone who simply is not signed in.
    retry: false,
  });
}
