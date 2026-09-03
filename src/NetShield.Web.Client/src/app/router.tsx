import type { QueryClient } from '@tanstack/react-query';
import { createRouter } from '@tanstack/react-router';

import { routeTree } from '@/routeTree.gen';

/** What every route's `beforeLoad` and `loader` is handed. */
export interface RouterContext {
  /**
   * The one owner of server state (ARCHITECTURE.md §9). The `_app` guard resolves the session
   * through it before the shell renders, so the answer is already in the cache by the time a
   * component asks for it — rather than every guarded page starting with a loading state.
   */
  readonly queryClient: QueryClient;
}

/** The router, built from the generated route tree in `src/routes`. */
export function createAppRouter(queryClient: QueryClient) {
  return createRouter({
    routeTree,
    context: { queryClient },
    defaultPreload: 'intent',
    scrollRestoration: true,
  });
}

declare module '@tanstack/react-router' {
  interface Register {
    router: ReturnType<typeof createAppRouter>;
  }
}
