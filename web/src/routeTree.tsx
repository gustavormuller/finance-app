import { createRootRoute, createRoute } from '@tanstack/react-router';

import VariantA from './design-preview/VariantA';
import VariantB from './design-preview/VariantB';
import VariantC from './design-preview/VariantC';
import VariantD from './design-preview/VariantD';
import VariantE from './design-preview/VariantE';
import VariantF from './design-preview/VariantF';
import HomePage from './routes/HomePage';
import LoginPage from './routes/LoginPage';
import ProtectedLayout from './routes/ProtectedLayout';

// No component: the default root renders an Outlet, and the application shell lives
// in App.tsx, outside the router.
const rootRoute = createRootRoute();

const loginRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/login',

  // The callback sends the browser here with ?error=unverified when Google will not
  // vouch for the address. Anything else in the query string is dropped rather than
  // rendered.
  //
  // The key is omitted rather than set to undefined: under exactOptionalPropertyTypes
  // a present-but-undefined key is still required, and navigating to /login would
  // have to pass a search object every time.
  validateSearch: (search: Record<string, unknown>): { error?: string } =>
    typeof search.error === 'string' ? { error: search.error } : {},

  component: LoginPage,
});

/**
 * A pathless layout route, so every page nested under it is guarded by construction
 * and adding a page cannot accidentally leave it public.
 */
const protectedRoute = createRoute({
  getParentRoute: () => rootRoute,
  id: 'protected',
  component: ProtectedLayout,
});

const homeRoute = createRoute({
  getParentRoute: () => protectedRoute,
  path: '/',
  component: HomePage,
});

/**
 * Throwaway. Six styling directions for the transactions list, rendered from one
 * static fixture so the screenshots are comparable and reproducible.
 *
 * Deliberately not behind the protected layout — they touch no API — and
 * deliberately not linked from anywhere: they are reached by typing the URL, seen
 * once, and deleted at the start of checkpoint 5.
 */
const designPreviewRoutes = (
  [
    ['a', VariantA],
    ['b', VariantB],
    ['c', VariantC],
    ['d', VariantD],
    ['e', VariantE],
    ['f', VariantF],
  ] as const
).map(([slug, component]) =>
  createRoute({
    getParentRoute: () => rootRoute,
    path: `/design-preview/${slug}`,
    component,
  }),
);

export const routeTree = rootRoute.addChildren([
  loginRoute,
  protectedRoute.addChildren([homeRoute]),
  ...designPreviewRoutes,
]);
