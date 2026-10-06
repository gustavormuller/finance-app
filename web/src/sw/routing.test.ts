import { describe, expect, it } from 'vitest';

import { cachesToDelete, routeFor } from './routing';

const ORIGIN = 'https://financas.example';

function route(path: string, mode: RequestMode = 'cors', method = 'GET') {
  return routeFor({ url: new URL(path, ORIGIN).href, mode, method }, ORIGIN);
}

describe('routeFor (022)', () => {
  /** Spec 022 test 1. */
  it('leaves the API and the health check to the network, sign-in navigations included', () => {
    expect(route('/api/auth/google/callback?code=4%2F0Ab&state=xyz', 'navigate')).toBe('network-only');
    expect(route('/api/auth/google', 'navigate')).toBe('network-only');
    expect(route('/api/transactions')).toBe('network-only');
    expect(route('/api/transactions', 'cors', 'POST')).toBe('network-only');
    expect(route('/api/auth/logout', 'cors', 'POST')).toBe('network-only');
    expect(route('/health')).toBe('network-only');
    expect(route('/health', 'navigate')).toBe('network-only');
  });

  it('matches the API as Caddy does: case-insensitive, on the decoded path', () => {
    expect(route('/API/Transactions')).toBe('network-only');
    expect(route('/%61pi/auth/me', 'navigate')).toBe('network-only');
  });

  it('answers the content-hashed build output from the cache', () => {
    expect(route('/assets/index-abc123.js')).toBe('cache-first');
    expect(route('/assets/index-abc123.css', 'no-cors')).toBe('cache-first');
    expect(route('/assets/manrope-latin-wght-normal-abc123.woff2')).toBe('cache-first');
  });

  it('serves app-route navigations network-first, with the shell behind them', () => {
    expect(route('/transactions?from=2026-09-01&to=2026-09-30', 'navigate')).toBe('network-first-shell');
    expect(route('/', 'navigate')).toBe('network-first-shell');
    expect(route('/investments/42', 'navigate')).toBe('network-first-shell');
    // A prefix of a segment is not the segment.
    expect(route('/apiary', 'navigate')).toBe('network-first-shell');
  });

  it('never touches another origin', () => {
    expect(
      routeFor(
        { url: 'https://accounts.google.com/o/oauth2/v2/auth?client_id=x', mode: 'navigate', method: 'GET' },
        ORIGIN,
      ),
    ).toBe('network-only');
    expect(routeFor({ url: 'https://cdn.example/assets/app.js', mode: 'cors', method: 'GET' }, ORIGIN)).toBe(
      'network-only',
    );
  });

  it('leaves the worker, the manifest, the icons and anything but a GET alone', () => {
    expect(route('/sw.js')).toBe('network-only');
    expect(route('/manifest.webmanifest')).toBe('network-only');
    expect(route('/icons/icon-192.png', 'no-cors')).toBe('network-only');
    expect(route('/assets/index-abc123.js', 'cors', 'HEAD')).toBe('network-only');
  });
});

describe('cachesToDelete (022)', () => {
  /** Spec 022 test 2. */
  it('keeps the current build and the one before it, and deletes the older ones', () => {
    expect(cachesToDelete(['finance-a'], 'finance-a')).toEqual([]);
    expect(cachesToDelete(['finance-a', 'finance-b'], 'finance-b')).toEqual([]);
    expect(cachesToDelete(['finance-a', 'finance-b', 'finance-c'], 'finance-c')).toEqual(['finance-a']);
    expect(cachesToDelete(['finance-a', 'finance-b', 'finance-c', 'finance-d'], 'finance-d')).toEqual([
      'finance-a',
      'finance-b',
    ]);
  });

  it('keeps the newest other build when an older one comes back', () => {
    // A rollback redeploys a build whose cache still exists.
    expect(cachesToDelete(['finance-a', 'finance-b'], 'finance-a')).toEqual([]);
    expect(cachesToDelete(['finance-a', 'finance-b', 'finance-c'], 'finance-a')).toEqual(['finance-b']);
  });

  it('never names a cache it does not own', () => {
    expect(cachesToDelete(['other', 'finance-a', 'finance-b', 'finance-c'], 'finance-c')).toEqual(['finance-a']);
  });
});
