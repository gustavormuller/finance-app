import type { ReactNode } from 'react';

/**
 * Not implemented yet: renders nothing, guards nothing.
 */
export default function RequireAuth({ children }: { children: ReactNode }) {
  void children;

  return null;
}
