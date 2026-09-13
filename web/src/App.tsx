import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

import HealthRoute from './routes/HealthRoute';

// The client lives here rather than in main.tsx so <App /> stays renderable on its
// own, which is what keeps App.test.tsx working without a wrapper.
const queryClient = new QueryClient();

export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <main>
        <h1>Personal Finance</h1>
        <HealthRoute />
      </main>
    </QueryClientProvider>
  );
}
