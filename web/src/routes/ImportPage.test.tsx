import { screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { account, batch, renderAt, stubAccountsApi } from './accounts-fixtures';

/**
 * `/import` (015): no longer a page, but the links and bookmarks to it keep working.
 * The wizard's own tests moved with it, to `components/import/AccountImport.test.tsx`.
 */
describe('/import', () => {
  afterEach(() => vi.unstubAllGlobals());

  const itau = account('acc-itau', 'Itaú');
  const nubank = account('acc-nubank', 'Nubank');

  /** Spec 015 test 16. */
  it('opens the import tab of the account whose statement is in review', async () => {
    stubAccountsApi({
      accounts: [itau, nubank],
      imports: [batch('b-done', itau), batch('b-open', nubank, { status: 'Staged', committedCount: null, committedAt: null })],
    });
    const router = renderAt('/import');

    await waitFor(() => expect(router.state.location.pathname).toBe('/accounts/acc-nubank'));
    expect(router.state.location.search).toEqual({ tab: 'import' });
    expect(await screen.findByRole('heading', { name: 'Nubank' })).toBeInTheDocument();
  });

  it('opens the accounts page, and so the first account, when nothing is in review', async () => {
    stubAccountsApi({ accounts: [itau, nubank], imports: [batch('b-done', nubank)] });
    const router = renderAt('/import');

    await waitFor(() => expect(router.state.location.pathname).toBe('/accounts/acc-itau'));
    expect(router.state.location.search).toEqual({});
  });
});
