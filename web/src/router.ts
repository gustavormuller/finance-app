import { createRouter } from '@tanstack/react-router';

import NotFoundPage from './routes/NotFoundPage';
import { routeTree } from './routeTree';

// Without it, an address that matches no page gets the library's English "Not Found".
export const router = createRouter({ routeTree, defaultNotFoundComponent: NotFoundPage });

// Makes `to` and search parameters typed against the real route tree, so a renamed
// route breaks the typecheck instead of the page.
declare module '@tanstack/react-router' {
  interface Register {
    router: typeof router;
  }
}
