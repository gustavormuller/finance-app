import { expect, test, type BrowserContext, type Page } from '@playwright/test';
import type { ChildProcess } from 'node:child_process';
import { existsSync, mkdtempSync, readdirSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';

import { createAccount, devLogin, uniqueEmail } from '../e2e/support';
import { BASE_URL, build, startPreview, stopPreview } from './preview';

/**
 * Spec 022 tests 8-12: the service worker against a production build. One page, in one
 * context, through the whole story: first visit, a signed-in session, the server going
 * down, and a deploy.
 */
test.describe.configure({ mode: 'serial' });

const DIST = new URL('../dist/', import.meta.url);

let server: ChildProcess | undefined;
let context: BrowserContext;
let page: Page;

test.beforeAll(async ({ browser }) => {
  build();
  server = await startPreview();
  context = await browser.newContext({ baseURL: BASE_URL });
  page = await context.newPage();
});

test.afterAll(async () => {
  await context?.close();
  if (server) {
    await stopPreview(server);
  }
});

/** Every entry in Cache Storage, as sorted paths, by cache in creation order. */
function cacheContents(): Promise<Record<string, string[]>> {
  return page.evaluate(async () => {
    const contents: Record<string, string[]> = {};
    for (const name of await caches.keys()) {
      const requests = await (await caches.open(name)).keys();
      contents[name] = requests.map((request) => new URL(request.url).pathname).sort();
    }
    return contents;
  });
}

async function goOffline() {
  await stopPreview(server!);
  server = undefined;
}

test('8: the worker registers and controls the page', async () => {
  await page.goto('/login');

  await expect
    .poll(() => page.evaluate(() => navigator.serviceWorker.controller?.scriptURL ?? null))
    .toBe(`${BASE_URL}/sw.js`);
});

test('9: nothing from /api or /health ever reaches Cache Storage', async () => {
  const email = uniqueEmail('pwa');
  const account = `Conta PWA ${Date.now()}`;

  await devLogin(page, email, 'Pessoa PWA');
  await createAccount(page, account, '1.234,56');
  for (const path of ['/', '/transactions', '/investments', '/accounts']) {
    await page.goto(path);
    await page.waitForLoadState('networkidle');
  }
  // Personal data did cross the worker's page.
  await expect(page.getByRole('link', { name: account })).toBeVisible();

  const contents = await cacheContents();
  const precache = ['/index.html', ...readdirSync(new URL('assets/', DIST)).map((file) => `/assets/${file}`)].sort();
  expect(Object.values(contents)).toEqual([precache]);
  expect(Object.values(contents).flat().filter((path) => /^\/(api|health)(\/|$)/i.test(path))).toEqual([]);

  const leaked = await page.evaluate(
    async (needles) => {
      const found = new Set<string>();
      for (const name of await caches.keys()) {
        const cache = await caches.open(name);
        for (const request of await cache.keys()) {
          const body = (await (await cache.match(request))?.text()) ?? '';
          needles.filter((needle) => body.includes(needle)).forEach((needle) => found.add(needle));
        }
      }
      return [...found];
    },
    [account, email],
  );
  expect(leaked).toEqual([]);
});

test('10: with the server down, an app route still opens on the app', async () => {
  await goOffline();

  const response = await page.goto('/transactions');

  expect(response?.fromServiceWorker()).toBe(true);
  await expect(page.getByRole('heading', { name: 'Finanças Pessoais' })).toBeVisible();
  // The app's own answer to an unreachable API.
  await expect(page.getByTestId('health-status')).toHaveText('degraded');

  server = await startPreview();
});

test('11: the browser finds the app installable', async ({ playwright }) => {
  // Chrome refuses to install from an incognito profile, which every Playwright context
  // but a persistent one is, and the headless shell answers "no errors" to anything.
  const profile = mkdtempSync(path.join(tmpdir(), 'pwa-install-'));
  const browser = await playwright.chromium.launchPersistentContext(profile, {
    channel: process.env.PWA_INSTALL_CHANNEL ?? 'chrome',
    baseURL: BASE_URL,
  });

  try {
    const installPage = await browser.newPage();
    await installPage.goto('/');
    const cdp = await browser.newCDPSession(installPage);

    const manifest = await cdp.send('Page.getAppManifest');
    expect(manifest.url).toBe(`${BASE_URL}/manifest.webmanifest`);
    expect(manifest.errors).toEqual([]);

    await expect
      .poll(async () => (await cdp.send('Page.getInstallabilityErrors')).installabilityErrors)
      .toEqual([]);
  } finally {
    await browser.close();
    rmSync(profile, { recursive: true, force: true });
  }
});

test('12: a new build takes over after one reload, and the old one keeps its chunks', async () => {
  const [first] = Object.keys(await cacheContents());
  const oldEntry = await page.evaluate(
    () => document.querySelector('script[type="module"][src^="/assets/"]')?.getAttribute('src') ?? '',
  );

  build('e2e-pwa/second-build.config.ts');
  // The deploy removed the running build's entry chunk from disk.
  expect(existsSync(new URL(`.${oldEntry}`, DIST))).toBe(false);

  await page.reload();
  await expect.poll(async () => Object.keys(await cacheContents())).toHaveLength(2);
  await expect
    .poll(() =>
      page.evaluate(async () => {
        const registration = await navigator.serviceWorker.getRegistration();
        return registration?.active?.state === 'activated' && !registration.installing && !registration.waiting;
      }),
    )
    .toBe(true);
  expect(Object.keys(await cacheContents())[0]).toBe(first);

  // A tab still running the first build loads its files from the kept cache.
  const old = await page.evaluate(async (src) => {
    const response = await fetch(src);
    return { status: response.status, type: response.headers.get('content-type'), body: await response.text() };
  }, oldEntry);
  expect(old.status).toBe(200);
  expect(old.type).toContain('javascript');
  expect(old.body).not.toContain('<!doctype html>');

  // Offline, the shell and the code are the second build's.
  await goOffline();
  await page.reload();
  await expect(page.locator('html')).toHaveAttribute('data-build', 'second');
});
