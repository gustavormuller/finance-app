/// <reference types="vitest/config" />
import tailwindcss from '@tailwindcss/vite';
import react from '@vitejs/plugin-react';
import { createHash } from 'node:crypto';
import path from 'node:path';
import { fileURLToPath, URL } from 'node:url';
import { build, defineConfig, type Plugin } from 'vite';

// The API port here must match api/Properties/launchSettings.json.
const API_URL = process.env.API_URL ?? 'http://localhost:5080';

/**
 * Emits dist/sw.js (022): src/sw/sw.ts bundled on its own as a classic script, given the
 * list of files this build emitted and a version that changes whenever any of them does.
 * index.html is hashed by content, so a change to it alone is a new version too.
 */
function serviceWorker(): Plugin {
  let outDir = '';

  return {
    name: 'finance-service-worker',
    apply: 'build',
    configResolved(config) {
      outDir = path.resolve(config.root, config.build.outDir);
    },
    async writeBundle(_options, bundle) {
      const files = Object.keys(bundle)
        .filter((file) => file === 'index.html' || file.startsWith('assets/'))
        .sort();

      const digest = createHash('sha256');
      for (const file of files) {
        const output = bundle[file]!;
        digest.update(`${file}\0`).update(output.type === 'chunk' ? output.code : output.source).update('\0');
      }

      await build({
        configFile: false,
        logLevel: 'warn',
        publicDir: false,
        define: {
          __SW_VERSION__: JSON.stringify(digest.digest('hex').slice(0, 16)),
          __SW_PRECACHE__: JSON.stringify(files.map((file) => `/${file}`)),
        },
        build: {
          outDir,
          emptyOutDir: false,
          lib: {
            entry: fileURLToPath(new URL('./src/sw/sw.ts', import.meta.url)),
            formats: ['iife'],
            name: 'financeServiceWorker',
            fileName: () => 'sw.js',
          },
        },
      });
    },
  };
}

export default defineConfig({
  plugins: [react(), tailwindcss(), serviceWorker()],

  // shadcn/ui generates imports as '@/components/...', so the alias has to exist
  // in both the bundler and the typechecker.
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  server: {
    port: 5173,
    strictPort: true,
    // changeOrigin is deliberately absent. It rewrites the Host header to the
    // target, and ASP.NET builds the Google redirect_uri from the incoming Host:
    // rewriting it produces http://localhost:5080/api/auth/google/callback, which
    // neither matches the Google Cloud Console entry nor lands the browser back in
    // the SPA. /health does not care, but the two blocks should not differ without
    // a reason. `vite preview` inherits this proxy.
    proxy: {
      '/api': {
        target: API_URL,
      },
      '/health': {
        target: API_URL,
      },
    },
  },
  test: {
    globals: true,
    environment: 'jsdom',
    setupFiles: ['./src/setupTests.ts'],
    // Vitest owns src/. Playwright owns e2e/. Without this split, `vitest run`
    // picks up the Playwright specs and fails on a missing test runner.
    include: ['src/**/*.{test,spec}.{ts,tsx}'],
    exclude: ['node_modules/**', 'dist/**', 'e2e/**'],
  },
});
