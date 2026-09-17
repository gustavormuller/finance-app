import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { RouterProvider } from '@tanstack/react-router';

import { router } from './router';
import HealthRoute from './routes/HealthRoute';

// The client lives here rather than in main.tsx so <App /> stays renderable on its
// own, which is what keeps App.test.tsx working without a wrapper.
const queryClient = new QueryClient();

/*
 * The application shell: a header that does not change per route, the router's
 * output, and the readiness readout pinned to the bottom.
 *
 * The heading and the readout sit outside the router on purpose. They render on the
 * first paint instead of after the router mounts, and an unauthenticated visit to /
 * ends up on /login — where 001's e2e specs still expect to find both after
 * navigating to /.
 *
 * The navigation is not here: it lives in ProtectedLayout, because it needs the
 * router's context and must not appear to someone who is not signed in. It is styled
 * as a second row of this header so the two read as one bar.
 */
export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <div className="bg-background text-foreground flex min-h-screen flex-col">
        <header className="bg-card border-b">
          <div className="mx-auto max-w-5xl px-4 py-3">
            <h1 className="text-base font-semibold tracking-tight">Finanças Pessoais</h1>
          </div>
        </header>

        <main className="flex-1">
          <RouterProvider router={router} />
        </main>

        <footer className="bg-card border-t">
          <div className="mx-auto max-w-5xl px-4 py-3">
            <HealthRoute />
          </div>
        </footer>
      </div>
    </QueryClientProvider>
  );
}
