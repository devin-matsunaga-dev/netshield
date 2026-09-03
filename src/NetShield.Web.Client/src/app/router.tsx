import { createRouter } from '@tanstack/react-router';

import { routeTree } from '@/routeTree.gen';

/** The router, built from the generated route tree in `src/routes`. */
export function createAppRouter() {
  return createRouter({
    routeTree,
    defaultPreload: 'intent',
    scrollRestoration: true,
  });
}

declare module '@tanstack/react-router' {
  interface Register {
    router: ReturnType<typeof createAppRouter>;
  }
}
