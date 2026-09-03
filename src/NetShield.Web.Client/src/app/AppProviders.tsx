import { QueryClientProvider } from '@tanstack/react-query';
import { RouterProvider } from '@tanstack/react-router';
import { useState } from 'react';

import { createAppRouter } from '@/app/router';
import { createQueryClient } from '@/lib/queryClient';

/**
 * The two providers the SPA runs inside: TanStack Query, which owns every piece of server state
 * (ARCHITECTURE.md §9), and TanStack Router — which is handed the query client, because the
 * route guard resolves the session before the shell renders.
 */
export function AppProviders() {
  const [queryClient] = useState(createQueryClient);
  const [router] = useState(() => createAppRouter(queryClient));

  return (
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>
  );
}
