import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { registerServiceWorker } from './serviceWorker';

describe('registerServiceWorker (022)', () => {
  const register = vi.fn(() => Promise.resolve());

  beforeEach(() => {
    // jsdom has no service worker container.
    Object.defineProperty(navigator, 'serviceWorker', { value: { register }, configurable: true });
  });

  afterEach(() => {
    Reflect.deleteProperty(navigator, 'serviceWorker');
    register.mockClear();
    vi.unstubAllEnvs();
  });

  /** Spec 022 test 6. */
  it('registers nothing outside a production build', () => {
    registerServiceWorker();
    window.dispatchEvent(new Event('load'));

    expect(register).not.toHaveBeenCalled();
  });

  it('registers /sw.js once the page has loaded, in a production build', () => {
    vi.stubEnv('PROD', true);

    registerServiceWorker();
    expect(register).not.toHaveBeenCalled();

    window.dispatchEvent(new Event('load'));
    expect(register).toHaveBeenCalledExactlyOnceWith('/sw.js');
  });
});
