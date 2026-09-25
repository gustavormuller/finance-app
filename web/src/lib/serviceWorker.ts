/**
 * Registers the service worker (022) in a production build only. The dev server has no
 * `sw.js`, and a worker there would stand between Vite's hot reload and the page.
 */
export function registerServiceWorker(): void {
  if (!import.meta.env.PROD || !('serviceWorker' in navigator)) {
    return;
  }

  // After load: installing the worker downloads the whole build, which must not compete
  // with the first paint.
  window.addEventListener(
    'load',
    () => {
      navigator.serviceWorker.register('/sw.js').catch(() => {
        // The app works without it; it only loses the offline shell.
      });
    },
    { once: true },
  );
}
