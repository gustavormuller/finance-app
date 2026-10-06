import { execFileSync, spawn, type ChildProcess } from 'node:child_process';
import { fileURLToPath } from 'node:url';

/**
 * The production build and `vite preview`, driven from the spec itself (022): it stops
 * the server to go offline and rebuilds to deploy, which a config-managed webServer
 * cannot do. `vite preview` inherits `server.proxy`, so /api reaches the API at API_URL.
 */
export const PORT = Number(process.env.PREVIEW_PORT ?? 4173);
export const BASE_URL = `http://localhost:${PORT}`;

const WEB = fileURLToPath(new URL('..', import.meta.url));

// Node runs Vite directly, so killing the child kills the server; no shell in between.
const VITE = fileURLToPath(new URL('../node_modules/vite/bin/vite.js', import.meta.url));

/** `vite build` into dist/; the typecheck is verify.sh's job. */
export function build(config?: string): void {
  const args = [VITE, 'build', '--logLevel', 'warn', ...(config === undefined ? [] : ['--config', config])];
  execFileSync(process.execPath, args, { cwd: WEB, stdio: 'inherit' });
}

export async function startPreview(): Promise<ChildProcess> {
  const server = spawn(process.execPath, [VITE, 'preview', '--port', String(PORT), '--strictPort'], {
    cwd: WEB,
    stdio: 'ignore',
  });
  await until(() => answers(true));
  return server;
}

export async function stopPreview(server: ChildProcess): Promise<void> {
  server.kill();
  await until(() => answers(false));
}

function answers(expected: boolean): Promise<boolean> {
  return fetch(BASE_URL).then(
    () => expected,
    () => !expected,
  );
}

async function until(condition: () => Promise<boolean>, timeoutMs = 30_000): Promise<void> {
  const deadline = Date.now() + timeoutMs;
  while (!(await condition())) {
    if (Date.now() > deadline) {
      throw new Error(`vite preview on ${BASE_URL} did not change state within ${timeoutMs} ms`);
    }
    await new Promise((resolve) => setTimeout(resolve, 200));
  }
}
