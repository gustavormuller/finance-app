/**
 * The service worker's decisions (022), free of worker APIs so a unit test can reach them.
 * `sw.ts` is the only caller.
 */

/**
 * `network-only` means the worker does not call `respondWith` at all: the browser handles
 * the request exactly as if there were no worker.
 */
export type Route = 'network-only' | 'cache-first' | 'network-first-shell';

/** Every cache the worker owns is named this plus the build's version. */
export const CACHE_PREFIX = 'finance-';

/** The page every client-side route is served by. */
export const SHELL = '/index.html';

export function routeFor(request: { url: string; mode: string; method: string }, origin: string): Route {
  if (request.method !== 'GET') {
    return 'network-only';
  }

  const url = new URL(request.url);

  if (url.origin !== origin) {
    return 'network-only';
  }

  // Sign-in, sign-out and every piece of personal data: the server's alone, even when it
  // cannot be reached. Checked before navigations, because the Google flow is one.
  if (servedByApi(url.pathname)) {
    return 'network-only';
  }

  if (request.mode === 'navigate') {
    return 'network-first-shell';
  }

  // Content-hashed by the build: a name never changes content, so a stored copy is never stale.
  if (url.pathname.startsWith('/assets/')) {
    return 'cache-first';
  }

  return 'network-only';
}

/**
 * Matched the way Caddy matches `/api/*` and `/health` before proxying them: without
 * regard to case, on the decoded path.
 */
function servedByApi(pathname: string): boolean {
  let path = pathname;
  try {
    path = decodeURIComponent(pathname);
  } catch {
    // A malformed escape: the raw path is still worth checking.
  }
  path = path.toLowerCase();

  return path === '/api' || path.startsWith('/api/') || path === '/health' || path.startsWith('/health/');
}

/**
 * The worker's caches to delete once `current` is active: all of its own but `current` and
 * the one created just before it, which is the build `current` replaces. That one stays a
 * generation longer, because a tab still running it is now controlled by the new worker
 * and loads its lazy chunks from there after the deploy deleted them from disk.
 *
 * `names` must be in creation order, which is how `CacheStorage.keys()` lists them.
 */
export function cachesToDelete(names: readonly string[], current: string): string[] {
  const others = names.filter((name) => name.startsWith(CACHE_PREFIX) && name !== current);

  return others.slice(0, -1);
}
