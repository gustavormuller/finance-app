/// <reference types="vitest/config" />
import tailwindcss from '@tailwindcss/vite';
import react from '@vitejs/plugin-react';
import { fileURLToPath, URL } from 'node:url';
import { defineConfig } from 'vite';

// The API port here must match api/Properties/launchSettings.json.
const API_URL = process.env.API_URL ?? 'http://localhost:5080';

export default defineConfig({
  plugins: [react(), tailwindcss()],

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
    // a reason.
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
