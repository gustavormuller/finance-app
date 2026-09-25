/// <reference lib="webworker" />
/*
 * The service worker (022). It keeps the build it belongs to in Cache Storage, answers
 * /assets/ from there, and falls back to that build's index.html when a navigation cannot
 * reach the server. The precache at install is the only write to Cache Storage: no API
 * response and no page the server sent is ever stored, so nobody's data outlives a
 * sign-out (ADR-013). Bundled on its own into dist/sw.js by the plugin in vite.config.ts.
 */
import { CACHE_PREFIX, SHELL, cachesToDelete, routeFor } from './routing';

declare const self: ServiceWorkerGlobalScope;

/** Written by the build: a digest of the files below, and the files, as root-relative URLs. */
declare const __SW_VERSION__: string;
declare const __SW_PRECACHE__: string[];

const CACHE = `${CACHE_PREFIX}${__SW_VERSION__}`;

/** How long a navigation waits for the server before the cached shell answers it. */
const NAVIGATION_TIMEOUT_MS = 3000;

self.addEventListener('install', (event) => {
  event.waitUntil(
    (async () => {
      const cache = await caches.open(CACHE);
      // Revalidated, not taken from the HTTP cache: a stale index.html there would name
      // another build's files.
      await cache.addAll(__SW_PRECACHE__.map((url) => new Request(url, { cache: 'no-cache' })));
      // A deploy takes over at once rather than when every tab has closed; the previous
      // build's cache survives activation for the tabs still running it.
      await self.skipWaiting();
    })(),
  );
});

self.addEventListener('activate', (event) => {
  event.waitUntil(
    (async () => {
      const stale = cachesToDelete(await caches.keys(), CACHE);
      await Promise.all(stale.map((name) => caches.delete(name)));
      await self.clients.claim();
    })(),
  );
});

self.addEventListener('fetch', (event) => {
  switch (routeFor(event.request, self.location.origin)) {
    case 'cache-first':
      event.respondWith(cacheFirst(event.request));
      break;
    case 'network-first-shell':
      event.respondWith(networkFirstShell(event.request));
      break;
    case 'network-only':
      break;
  }
});

/** Any of the worker's caches will do: the names are content-hashed. Nothing is stored. */
async function cacheFirst(request: Request): Promise<Response> {
  return (await caches.match(request)) ?? fetch(request);
}

async function networkFirstShell(request: Request): Promise<Response> {
  const network = fetch(request);
  const answer = await Promise.race([
    network.catch(() => undefined),
    new Promise<undefined>((resolve) => setTimeout(resolve, NAVIGATION_TIMEOUT_MS)),
  ]);

  // Every app route is index.html, so a 5xx is the tunnel or the proxy, not the app.
  if (answer !== undefined && answer.status < 500) {
    return answer;
  }

  // This build's own shell, not whichever cache happens to hold an index.html: it names
  // exactly the files precached beside it.
  const shell = await (await caches.open(CACHE)).match(SHELL);

  return shell ?? answer ?? network;
}
