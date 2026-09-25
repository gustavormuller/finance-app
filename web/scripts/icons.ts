/**
 * Rasterises the icon sources in public/ into the PNGs the manifest and index.html name
 * (022). Chromium draws them, through the Playwright the E2E suite already installs, so
 * no image library is needed. Run from web/ after editing an SVG, on Node 22.18 or later:
 *
 *   node scripts/icons.ts
 */
import { chromium } from '@playwright/test';
import { readFile, writeFile } from 'node:fs/promises';

const PUBLIC = new URL('../public/', import.meta.url);

const OUTPUTS: [source: string, target: string, size: number][] = [
  ['favicon.svg', 'icons/favicon-32.png', 32],
  ['favicon.svg', 'icons/icon-192.png', 192],
  ['favicon.svg', 'icons/icon-512.png', 512],
  ['icons/icon-maskable.svg', 'icons/icon-maskable-512.png', 512],
  ['icons/icon-maskable.svg', 'icons/apple-touch-icon.png', 180],
];

const browser = await chromium.launch();

try {
  const page = await browser.newPage({ deviceScaleFactor: 1 });

  for (const [source, target, size] of OUTPUTS) {
    const svg = await readFile(new URL(source, PUBLIC), 'utf8');

    await page.setViewportSize({ width: size, height: size });
    await page.setContent(
      `<style>html,body{margin:0;background:transparent}svg{display:block;width:${size}px;height:${size}px}</style>${svg}`,
    );
    await writeFile(new URL(target, PUBLIC), await page.screenshot({ omitBackground: true }));

    console.log(`${target} ${size}x${size}`);
  }
} finally {
  await browser.close();
}
