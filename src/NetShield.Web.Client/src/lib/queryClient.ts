import { QueryClient } from '@tanstack/react-query';

/**
 * The single owner of server state (ARCHITECTURE.md §9). Nothing that came from the API is kept
 * in a store, a context or component state.
 */
export function createQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: {
        // A network operations console is read constantly and mostly by people watching a
        // screen. Half a minute of staleness is short enough to feel live and long enough that
        // moving between routes does not refetch everything.
        staleTime: 30_000,
        // A 401 or a 403 is an answer, not a fault; retrying either wastes a round trip and,
        // for a locked account, spends an attempt.
        retry: (failureCount, error) => failureCount < 2 && !isClientError(error),
        refetchOnWindowFocus: true,
      },
      mutations: {
        retry: false,
      },
    },
  });
}

function isClientError(error: unknown): boolean {
  return (
    typeof error === 'object' &&
    error !== null &&
    'status' in error &&
    typeof error.status === 'number' &&
    error.status >= 400 &&
    error.status < 500
  );
}
