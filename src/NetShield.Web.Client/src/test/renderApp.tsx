import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { createMemoryHistory, createRouter, RouterProvider } from '@tanstack/react-router';
import { render, type RenderResult } from '@testing-library/react';

import { routeTree } from '@/routeTree.gen';

type AppRouter = ReturnType<typeof createAppTestRouter>;

interface RenderedApp extends RenderResult {
  /** For a test that has to move the way a link would, to a route nothing links to. */
  readonly router: AppRouter;
  readonly queryClient: QueryClient;
}

/**
 * Renders the real application at a given URL: the real route tree, the real guard, the real
 * shell, the real generated client, and MSW answering the API. Tests then read the DOM, which is
 * the only thing a user can read (CONVENTIONS.md §7).
 */
export function renderApp(path = '/overview'): RenderedApp {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });

  const router = createAppTestRouter(queryClient, path);

  return {
    ...render(
      <QueryClientProvider client={queryClient}>
        {/* The memory router's type differs from the application router's registered one. */}
        <RouterProvider router={router as never} />
      </QueryClientProvider>,
    ),
    router,
    queryClient,
  };
}

function createAppTestRouter(queryClient: QueryClient, path: string) {
  return createRouter({
    routeTree,
    context: { queryClient },
    history: createMemoryHistory({ initialEntries: [path] }),
    defaultPendingMinMs: 0,
  });
}
