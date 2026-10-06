// @vitest-environment node
import { readFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';

import { compile } from 'tailwindcss';
import { describe, expect, it } from 'vitest';

/*
 * Read from disk: Vitest turns every CSS import into an empty string, `?raw` included. A
 * `*.node.test.ts` file is typechecked with Node's types (tsconfig.node.json), not the app's.
 */

/** The stylesheets `index.css` imports, where the Vite plugin would find them. */
const STYLESHEETS: Record<string, string> = {
  tailwindcss: '../node_modules/tailwindcss/index.css',
  'tw-animate-css': '../node_modules/tw-animate-css/dist/tw-animate.css',
};

async function buildCss(candidates: string[]): Promise<string> {
  const compiler = await compile(await readFile(new URL('./index.css', import.meta.url), 'utf8'), {
    loadStylesheet: async (id, base) => {
      const url = new URL(STYLESHEETS[id] ?? id, import.meta.url);
      return { path: fileURLToPath(url), base, content: await readFile(url, 'utf8') };
    },
  });

  return compiler.build(candidates);
}

/** Where a utility's rule starts in the built CSS; `/` is escaped in a selector. */
function ruleAt(css: string, candidate: string): number {
  const at = css.indexOf(`.${candidate.replace('/', '\\/')} {`);
  expect(at, `no rule for ${candidate}`).toBeGreaterThanOrEqual(0);

  return at;
}

/*
 * 012 amendment 1, test 8. Utilities share a layer and a specificity, so the later rule
 * wins: a border utility written beside `glass` has to come after it. With the `border`
 * shorthand, `.glass` sorted after all of them and `border-primary` showed `--border`.
 */
describe('glass', () => {
  it.each(['border-primary', 'border-destructive/40', 'border-2', 'border-dashed'])(
    'lets %s beside it win',
    async (utility) => {
      const css = await buildCss(['glass', utility]);

      expect(ruleAt(css, utility)).toBeGreaterThan(ruleAt(css, 'glass'));
    },
  );
});
