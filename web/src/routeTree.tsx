import { createRootRoute, createRoute, lazyRouteComponent } from '@tanstack/react-router';

import { accountTabs, type AccountTab } from './lib/accounts';
import LoginPage from './routes/LoginPage';
import ProtectedLayout from './routes/ProtectedLayout';

// 018: each page is its own chunk, fetched when the route is first entered, so the
// sign-in page and the shell do not wait for every page and the charts library. The
// sign-in page and the layout stay in the entry chunk: one is the first thing a visitor
// sees, the other frames every page.
const AccountPage = lazyRouteComponent(() => import('./routes/AccountPage'));
const AccountsPage = lazyRouteComponent(() => import('./routes/AccountsPage'));
const FirstAccount = lazyRouteComponent(() => import('./routes/AccountsPage'), 'FirstAccount');
const AssetPage = lazyRouteComponent(() => import('./routes/AssetPage'));
const AssetReturnsPage = lazyRouteComponent(() => import('./routes/AssetReturnsPage'));
const CategoriesPage = lazyRouteComponent(() => import('./routes/CategoriesPage'));
const DashboardPage = lazyRouteComponent(() => import('./routes/DashboardPage'));
const ImportPage = lazyRouteComponent(() => import('./routes/ImportPage'));
const InvestmentsPage = lazyRouteComponent(() => import('./routes/InvestmentsPage'));
const MarketDataPage = lazyRouteComponent(() => import('./routes/MarketDataPage'));
const ReturnsPage = lazyRouteComponent(() => import('./routes/ReturnsPage'));
const SettingsPage = lazyRouteComponent(() => import('./routes/SettingsPage'));
const TransactionsPage = lazyRouteComponent(() => import('./routes/TransactionsPage'));

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

// 005: the dashboard is the landing page after sign-in.
const homeRoute = createRoute({
  getParentRoute: () => protectedRoute,
  path: '/',
  component: DashboardPage,
});

// 003's three screens, all nested under the pathless protected layout so they are
// guarded by construction rather than by each page remembering to check.
const transactionsRoute = createRoute({
  getParentRoute: () => protectedRoute,
  path: '/transactions',

  // 004's done step links here with the batch it just wrote, so the list opens on
  // exactly those rows; 015's account page with the account. Anything else in the
  // query string is dropped.
  validateSearch: (search: Record<string, unknown>): { importBatchId?: string; accountId?: string } => ({
    ...(typeof search.importBatchId === 'string' ? { importBatchId: search.importBatchId } : {}),
    ...(typeof search.accountId === 'string' ? { accountId: search.accountId } : {}),
  }),

  component: TransactionsPage,
});

const importRoute = createRoute({
  getParentRoute: () => protectedRoute,
  path: '/import',
  component: ImportPage,
});

// 015: the accounts beside the selected one, whose tab is a search parameter so a
// reload or a link opens it. Declared here, it is inherited by both children.
const accountsRoute = createRoute({
  getParentRoute: () => protectedRoute,
  path: '/accounts',
  validateSearch: (search: Record<string, unknown>): { tab?: AccountTab } =>
    accountTabs.includes(search.tab as AccountTab) && search.tab !== 'transactions'
      ? { tab: search.tab as AccountTab }
      : {},
  component: AccountsPage,
});

const accountsIndexRoute = createRoute({
  getParentRoute: () => accountsRoute,
  path: '/',
  component: FirstAccount,
});

const accountRoute = createRoute({
  getParentRoute: () => accountsRoute,
  path: '$accountId',
  component: AccountPage,
});

const categoriesRoute = createRoute({
  getParentRoute: () => protectedRoute,
  path: '/categories',
  component: CategoriesPage,
});

// 006: not in the navigation (spec: reachable from settings later), but guarded like
// every other page.
const marketDataRoute = createRoute({
  getParentRoute: () => protectedRoute,
  path: '/market-data',
  component: MarketDataPage,
});

// 007: in the navigation (spec: "Route `/investments`, in the nav").
const investmentsRoute = createRoute({
  getParentRoute: () => protectedRoute,
  path: '/investments',
  component: InvestmentsPage,
});

// 008: "linked from the positions page", not the navigation. A static segment, so it
// ranks above `$assetId` and no asset id can shadow it.
const returnsRoute = createRoute({
  getParentRoute: () => protectedRoute,
  path: '/investments/returns',
  component: ReturnsPage,
});

const assetRoute = createRoute({
  getParentRoute: () => protectedRoute,
  path: '/investments/$assetId',
  component: AssetPage,
});

// 008: "click through to the asset's own returns page".
const assetReturnsRoute = createRoute({
  getParentRoute: () => protectedRoute,
  path: '/investments/$assetId/returns',
  component: AssetReturnsPage,
});

// 009: "new route, in the nav".
const settingsRoute = createRoute({
  getParentRoute: () => protectedRoute,
  path: '/settings',
  component: SettingsPage,
});

export const routeTree = rootRoute.addChildren([
  loginRoute,
  protectedRoute.addChildren([homeRoute, transactionsRoute, importRoute, accountsRoute.addChildren([accountsIndexRoute, accountRoute]), categoriesRoute, marketDataRoute, investmentsRoute, returnsRoute, assetRoute, assetReturnsRoute, settingsRoute]),
]);
