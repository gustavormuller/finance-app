import { describe, expect, it } from 'vitest';

import indexHtml from '../index.html?raw';
import manifestSource from '../public/manifest.webmanifest?raw';
import css from './index.css?raw';

/** Every file in public/, keyed as `/path` the way the browser asks for it. */
const PUBLIC_FILES = new Set(
  Object.keys(import.meta.glob('../public/**/*')).map((file) => file.replace('../public', '')),
);

/** Each PNG in public/ as a data URL, to read its real pixel size. */
const PNGS = Object.fromEntries(
  Object.entries(
    import.meta.glob<string>('../public/**/*.png', { query: '?inline', import: 'default', eager: true }),
  ).map(([file, dataUrl]) => [file.replace('../public', ''), dataUrl]),
);

/** Width x height from a PNG's IHDR chunk. */
function pngSize(path: string): string {
  const dataUrl = PNGS[path];
  if (dataUrl === undefined) {
    throw new Error(`${path} is not a PNG in public/`);
  }
  const bytes = Uint8Array.from(atob(dataUrl.slice(dataUrl.indexOf(',') + 1)), (char) => char.charCodeAt(0));
  const view = new DataView(bytes.buffer);

  return `${view.getUint32(16)}x${view.getUint32(20)}`;
}

/** A `--name: value` token from the given block of index.css (`:root` or `.dark`). */
function token(block: ':root' | '.dark', name: string): string | undefined {
  const body = css.slice(css.indexOf(`${block} {`));
  return new RegExp(`--${name}:\\s*([^;]+);`).exec(body.slice(0, body.indexOf('}')))?.[1];
}

type Icon = { src: string; sizes: string; type: string; purpose?: string };

const manifest = JSON.parse(manifestSource) as Record<string, unknown> & { icons: Icon[] };

const head = new DOMParser().parseFromString(indexHtml, 'text/html').head;

describe('the web app manifest (022)', () => {
  /** Spec 022 test 3. */
  it('names the app in Portuguese and opens it standalone from the root', () => {
    expect(manifest).toMatchObject({
      id: '/',
      name: 'Finanças Pessoais',
      short_name: 'Finanças',
      lang: 'pt-BR',
      start_url: '/',
      scope: '/',
      display: 'standalone',
    });
    expect((manifest.short_name as string).length).toBeLessThanOrEqual(12);
  });

  it('names icons that exist, at the sizes they declare', () => {
    const declared = manifest.icons.map((icon) => `${icon.sizes} ${icon.purpose ?? 'any'}`);
    expect(declared).toEqual(expect.arrayContaining(['192x192 any', '512x512 any', '512x512 maskable']));

    for (const icon of manifest.icons) {
      expect(PUBLIC_FILES).toContain(icon.src);
      expect(icon.type).toBe('image/png');
      expect(pngSize(icon.src)).toBe(icon.sizes);
    }
  });

  it('opens on the dark ground, the one colour no theme flashes against', () => {
    expect(token('.dark', 'background')).toBe('#0b0c12');
    expect(manifest.background_color).toBe(token('.dark', 'background'));
    expect(manifest.theme_color).toBe(token('.dark', 'background'));
  });
});

describe('index.html (022)', () => {
  /** Spec 022 test 4. */
  it('links the manifest, the favicons and the Apple icon, all of which exist', () => {
    const href = (selector: string) => head.querySelector(selector)?.getAttribute('href');

    expect(href('link[rel="manifest"]')).toBe('/manifest.webmanifest');
    expect(PUBLIC_FILES).toContain(href('link[rel="icon"][type="image/svg+xml"]'));

    const png = head.querySelector('link[rel="icon"][type="image/png"]');
    expect(PUBLIC_FILES).toContain(png?.getAttribute('href'));
    expect(pngSize(png?.getAttribute('href') ?? '')).toBe(png?.getAttribute('sizes'));

    const apple = href('link[rel="apple-touch-icon"]') ?? '';
    expect(pngSize(apple)).toBe('180x180');
  });

  it('carries the iOS metas', () => {
    const meta = (name: string) => head.querySelector(`meta[name="${name}"]`)?.getAttribute('content');

    expect(meta('apple-mobile-web-app-capable')).toBe('yes');
    expect(meta('apple-mobile-web-app-title')).toBe('Finanças');
    expect(meta('apple-mobile-web-app-status-bar-style')).toBe('default');
  });

  it("colours the browser's toolbar with each theme's ground", () => {
    const themeColor = (scheme: string) =>
      head
        .querySelector(`meta[name="theme-color"][media="(prefers-color-scheme: ${scheme})"]`)
        ?.getAttribute('content');

    expect(themeColor('light')).toBe(token(':root', 'background'));
    expect(themeColor('dark')).toBe(token('.dark', 'background'));
  });
});
