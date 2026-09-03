import { useMutation, useQueryClient, type UseMutationResult } from '@tanstack/react-query';

import { api } from '@/api/client';
import type { AuthenticatedUser } from '@/api/types';
import { sessionKeys } from '@/features/session/api/sessionKeys';

/** What the sign-in form sends. Never a query key, never a cache entry, never a log line. */
export interface Credentials {
  readonly username: string;
  readonly password: string;
}

/**
 * The one message a refused sign-in produces.
 *
 * WP-0.4 answers an unknown username, a wrong password, a disabled account and a locked-out
 * account with one identical 401, on purpose. Saying more here would undo that from the client
 * side — the wording has to be as uninformative as the status code.
 */
export const signInRefused = 'That username and password do not match an account.';

/** Anything else that stopped the request reaching a verdict. */
export const signInUnavailable = 'Could not reach NetShield. Check your connection and try again.';

/**
 * Signs in. The session and refresh cookies arrive on the response and are set by the browser;
 * nothing about them is readable from here, which is the point of `HttpOnly`.
 */
export function useLogin(): UseMutationResult<AuthenticatedUser, Error, Credentials> {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async ({ username, password }: Credentials) => {
      const { data, response } = await api.POST('/api/v1/auth/login', {
        body: { username, password },
      });

      if (response.status === 401) {
        throw new Error(signInRefused);
      }

      if (!response.ok || data === undefined) {
        throw new Error(signInUnavailable);
      }

      return data;
    },
    onSuccess: (user) => {
      // Seed rather than invalidate: login already returned the user, and a refetch would put a
      // loading state between the sign-in and the screen the user asked for.
      queryClient.setQueryData(sessionKeys.current(), user);
    },
  });
}
