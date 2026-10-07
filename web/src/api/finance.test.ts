import { afterEach, describe, expect, it, vi } from 'vitest';

import { api, ApiError } from './finance';

/**
 * Spec 028: no page shows a 400's message on its own, so E2E never sees the title it would
 * fall back to. ASP.NET writes it in English over the pt-BR field messages.
 */
describe('a refusal', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("reads a 400's field messages, never its English title", async () => {
    const errors = { name: ['O nome é obrigatório.'], parentId: ['Categoria não encontrada.'] };
    vi.stubGlobal(
      'fetch',
      vi.fn(
        async () =>
          new Response(JSON.stringify({ title: 'One or more validation errors occurred.', status: 400, errors }), {
            status: 400,
            headers: { 'Content-Type': 'application/problem+json' },
          }),
      ),
    );

    const refused = api.createCategory({ name: ' ', kind: 'Expense', parentId: null });

    await expect(refused).rejects.toBeInstanceOf(ApiError);
    await expect(refused).rejects.toMatchObject({
      status: 400,
      message: 'O nome é obrigatório. Categoria não encontrada.',
      fields: errors,
    });
  });
});
