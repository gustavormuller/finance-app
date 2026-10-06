import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { RouterProvider } from '@tanstack/react-router';

import ThemeToggle from './components/ThemeToggle';
import { router } from './router';
import HealthRoute from './routes/HealthRoute';

// The client lives here rather than in main.tsx so <App /> stays renderable on its
// own, which is what keeps App.test.tsx working without a wrapper.
const queryClient = new QueryClient();

/*
 * The heading and the readout sit outside the router on purpose. They render on the
 * first paint instead of after the router mounts, and an unauthenticated visit to /
 * ends up on /login — where the smoke and health e2e specs still expect to find both
 * after navigating to /. The theme choice is here too, for the same reason: it has to
 * be reachable on the sign-in page as well.
 *
 * The navigation is not here: it lives in ProtectedLayout, because it needs the
 * router's context and must not appear to someone who is not signed in.
 */
export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <div className="text-foreground flex min-h-screen flex-col">
        <header className="px-4 pt-5 pb-4 sm:px-6 lg:px-8">
          <div className="flex items-center gap-4">
            <h1 className="font-display text-xl font-semibold tracking-tight">
              Finanças Pessoais
              <span aria-hidden="true" className="text-primary">
                .
              </span>
            </h1>
            <ThemeToggle className="ml-auto" />
          </div>
        </header>

        <main className="flex-1">
          <RouterProvider router={router} />
        </main>

        <footer className="px-4 py-4 sm:px-6 lg:px-8">
          <HealthRoute />
        </footer>
      </div>
    </QueryClientProvider>
  );
}
