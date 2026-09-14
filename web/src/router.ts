import { createRouter } from '@tanstack/react-router';

import { routeTree } from './routeTree';

export const router = createRouter({ routeTree });

// Makes `to` and search parameters typed against the real route tree, so a renamed
// route breaks the typecheck instead of the page.
declare module '@tanstack/react-router' {
  interface Register {
    router: typeof router;
  }
}
