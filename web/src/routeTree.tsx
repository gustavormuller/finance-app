import { createRootRoute, createRoute } from '@tanstack/react-router';

import AccountsPage from './routes/AccountsPage';
import CategoriesPage from './routes/CategoriesPage';
import HomePage from './routes/HomePage';
import ImportPage from './routes/ImportPage';
import LoginPage from './routes/LoginPage';
import ProtectedLayout from './routes/ProtectedLayout';
import TransactionsPage from './routes/TransactionsPage';

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

// 003's three screens, all nested under the pathless protected layout so they are
// guarded by construction rather than by each page remembering to check.
const transactionsRoute = createRoute({
  getParentRoute: () => protectedRoute,
  path: '/transactions',

  // 004's done step links here with the batch it just wrote, so the list opens on
  // exactly those rows. Anything else in the query string is dropped.
  validateSearch: (search: Record<string, unknown>): { importBatchId?: string } =>
    typeof search.importBatchId === 'string' ? { importBatchId: search.importBatchId } : {},

  component: TransactionsPage,
});

const importRoute = createRoute({
  getParentRoute: () => protectedRoute,
  path: '/import',
  component: ImportPage,
});

const accountsRoute = createRoute({
  getParentRoute: () => protectedRoute,
  path: '/accounts',
  component: AccountsPage,
});

const categoriesRoute = createRoute({
  getParentRoute: () => protectedRoute,
  path: '/categories',
  component: CategoriesPage,
});

export const routeTree = rootRoute.addChildren([
  loginRoute,
  protectedRoute.addChildren([homeRoute, transactionsRoute, importRoute, accountsRoute, categoriesRoute]),
]);
