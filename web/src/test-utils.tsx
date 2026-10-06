import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render } from '@testing-library/react';
import { vi } from 'vitest';

/** A request the stub saw: method, URL and the parsed JSON body, if any. */
export interface SeenRequest {
  method: string;
  url: string;
  body: unknown;
}

/** `body` is sent as JSON; `text` is sent as it is, with `headers`, for a file. */
export type StubAnswer = { status?: number; body?: unknown; text?: string; headers?: Record<string, string> };

type Responder = (request: SeenRequest) => StubAnswer | undefined;

/**
 * Replaces `fetch` with one that answers from `respond` and records every request.
 * A request `respond` does not answer fails loudly, so a test never passes on a call
 * it did not expect.
 */
export function stubFetch(respond: Responder) {
  const seen: SeenRequest[] = [];

  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const request: SeenRequest = {
        method: init?.method ?? 'GET',
        url: typeof input === 'string' ? input : input.toString(),
        body: typeof init?.body === 'string' ? JSON.parse(init.body) : undefined,
      };
      seen.push(request);

      const answer = respond(request);

      if (!answer) {
        throw new Error(`unexpected ${request.method} ${request.url}`);
      }

      const status = answer.status ?? 200;

      if (answer.text !== undefined) {
        return new Response(answer.text, { status, headers: answer.headers ?? {} });
      }

      return new Response(status === 204 ? null : JSON.stringify(answer.body ?? null), {
        status,
        headers: { 'Content-Type': 'application/json' },
      });
    }),
  );

  return seen;
}

/** Renders inside a fresh query client that never retries, so a failure shows at once. */
export function renderWithClient(ui: React.ReactElement) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  return render(<QueryClientProvider client={client}>{ui}</QueryClientProvider>);
}
