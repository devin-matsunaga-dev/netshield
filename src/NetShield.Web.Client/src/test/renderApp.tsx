import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { createMemoryHistory, createRouter, RouterProvider } from '@tanstack/react-router';
import { render, type RenderResult } from '@testing-library/react';

import { routeTree } from '@/routeTree.gen';

/**
 * Renders the real application at a given URL: the real route tree, the real shell, the real
 * generated client, and MSW answering the API. Tests then read the DOM, which is the only thing
 * a user can read (CONVENTIONS.md §7).
 */
export function renderApp(path = '/overview'): RenderResult {
  const router = createRouter({
    routeTree,
    history: createMemoryHistory({ initialEntries: [path] }),
    defaultPendingMinMs: 0,
  });

  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });

  return render(
    <QueryClientProvider client={queryClient}>
      {/* The memory router's type differs from the application router's registered one. */}
      <RouterProvider router={router as never} />
    </QueryClientProvider>,
  );
}
