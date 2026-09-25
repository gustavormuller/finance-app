import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { RouterProvider, createMemoryHistory, createRouter } from '@tanstack/react-router';
import { render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { routeTree } from '../routeTree';
import '../test-routes';

const signedInUser = {
  id: '3f1b2c4d-0000-4000-8000-000000000001',
  email: 'ada@example.com',
  displayName: 'Ada Lovelace',
  aiEnabled: false,
};

/**
 * Answers /api/auth/me with one response, makes any other request obvious, and hands
 * back the spy so a test can wait for the request to have actually been issued.
 */
function stubMe(respond: () => Promise<Response>) {
  const fetchMe = vi.fn(async (input: RequestInfo | URL) => {
    const url = typeof input === 'string' ? input : input.toString();

    if (!url.includes('/api/auth/me')) {
      throw new Error(`unexpected request to ${url}`);
    }

    return respond();
  });

  vi.stubGlobal('fetch', fetchMe);

  return fetchMe;
}

function stubMeStatus(status: number, body: unknown) {
  return stubMe(
    async () =>
      new Response(body === null ? null : JSON.stringify(body), {
        status,
        headers: { 'Content-Type': 'application/json' },
      }),
  );
}

/**
 * Renders the real route tree over an in-memory history, so what is under test is the
 * routing as it ships — the guard attached to the layout route, and the search
 * parameters the login route declares — rather than a hand-assembled approximation.
 */
function renderAt(path: string) {
  const router = createRouter({
    routeTree,
    history: createMemoryHistory({ initialEntries: [path] }),
  });

  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });

  render(
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  );

  return router;
}

describe('the protected layout', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('renders the page when /api/auth/me resolves 200', async () => {
    stubMeStatus(200, signedInUser);

    renderAt('/');

    expect(await screen.findByTestId('current-user')).toHaveTextContent('Ada Lovelace');
  });

  /** Spec 015 test 17: the import lives in each account, so it has no link of its own. */
  it('offers the accounts in the navigation, and no Importar', async () => {
    stubMeStatus(200, signedInUser);

    renderAt('/');

    const navigation = await screen.findByRole('navigation', { name: 'Principal' });
    expect(within(navigation).getByRole('link', { name: 'Contas' })).toHaveAttribute('href', '/accounts');
    expect(within(navigation).queryByRole('link', { name: 'Importar' })).not.toBeInTheDocument();
  });

  it('redirects to /login when /api/auth/me resolves 401', async () => {
    stubMeStatus(401, null);

    const router = renderAt('/');

    await waitFor(() => {
      expect(router.state.location.pathname).toBe('/login');
    });
  });

  it('renders neither the page nor the login page while /api/auth/me is in flight', async () => {
    // Never settles, so the loading state is the only state this test can observe.
    const fetchMe = stubMe(() => new Promise<Response>(() => {}));

    const router = renderAt('/');

    // The request having been issued is what proves the layout mounted and is
    // genuinely waiting, rather than the assertions below passing on an empty frame.
    await waitFor(() => {
      expect(fetchMe).toHaveBeenCalled();
    });

    // Flashing the login page at someone who turns out to be signed in is worse than
    // showing nothing for a moment.
    expect(screen.queryByRole('link', { name: 'Entrar com o Google' })).not.toBeInTheDocument();
    expect(screen.queryByTestId('current-user')).not.toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/');
  });
});

describe('the login page', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('explains itself when the callback reports an unverified address', async () => {
    stubMeStatus(401, null);

    renderAt('/login?error=unverified');

    expect(await screen.findByRole('alert')).toHaveTextContent(/não está verificado/i);
    expect(screen.getByRole('link', { name: 'Entrar com o Google' })).toBeInTheDocument();
  });

  it.each([
    ['cancelled', /cancelada/i],
    ['auth_failed', /não foi possível concluir/i],
  ])('explains a %s callback', async (code, expected) => {
    stubMeStatus(401, null);

    renderAt(`/login?error=${code}`);

    expect(await screen.findByRole('alert')).toHaveTextContent(expected);
  });

  it('shows no message when there is no error in the query string', async () => {
    stubMeStatus(401, null);

    renderAt('/login');

    expect(await screen.findByRole('link', { name: 'Entrar com o Google' })).toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('shows no message for an error code it does not recognise', async () => {
    stubMeStatus(401, null);

    // A stale bookmark must not render an empty alert box.
    renderAt('/login?error=something-else');

    expect(await screen.findByRole('link', { name: 'Entrar com o Google' })).toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });
});
