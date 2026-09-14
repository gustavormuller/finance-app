import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { RouterProvider } from '@tanstack/react-router';

import { router } from './router';
import HealthRoute from './routes/HealthRoute';

// The client lives here rather than in main.tsx so <App /> stays renderable on its
// own, which is what keeps App.test.tsx working without a wrapper.
const queryClient = new QueryClient();

/*
 * The heading and the readiness readout sit outside the router on purpose. They are
 * chrome that does not change per route, they render on the first paint instead of
 * after the router mounts, and an unauthenticated visit to / now ends up on /login —
 * where 001's e2e specs still expect to find both after navigating to /.
 */
export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <main>
        <h1>Personal Finance</h1>
        <RouterProvider router={router} />
        <HealthRoute />
      </main>
    </QueryClientProvider>
  );
}
