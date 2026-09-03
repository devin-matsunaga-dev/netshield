import { useMutation, useQueryClient, type UseMutationResult } from '@tanstack/react-query';
import { useNavigate } from '@tanstack/react-router';

import { api } from '@/api/client';

/**
 * Ends the session.
 *
 * The API revokes the refresh chain and clears both cookies. The cache is emptied whatever the
 * API answered — a request that failed to reach it has still ended the session as far as this
 * browser is concerned, and leaving the last user's data in the cache for the next one is the
 * worse of the two mistakes.
 *
 * Emptied *before* the navigation, not after: the sign-in route decides whether to bounce a
 * caller by reading the cached session, so a cache still holding one would send the user
 * straight back to the screen they just left.
 */
export function useLogout(): UseMutationResult<void, Error, void> {
  const queryClient = useQueryClient();
  const navigate = useNavigate();

  return useMutation({
    mutationFn: async () => {
      await api.POST('/api/v1/auth/logout');
    },
    onSettled: async () => {
      queryClient.clear();

      await navigate({ to: '/login', replace: true });
    },
  });
}
