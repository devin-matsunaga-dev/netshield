import { queryOptions } from '@tanstack/react-query';

import { api } from '@/api/client';
import type { AuthenticatedUser } from '@/api/types';
import { sessionKeys } from '@/features/session/api/sessionKeys';

/** An API call that failed, carrying the status the guard reads to tell 401 from everything else. */
export class ApiError extends Error {
  public constructor(
    message: string,
    public readonly status: number,
  ) {
    super(message);
    this.name = 'ApiError';
  }
}

/**
 * Who the caller is, from `GET /api/v1/auth/me`.
 *
 * Read rather than assembled from anything the client holds: the account may have been disabled,
 * renamed or told to change its password since the cookie was minted, and the `_app` guard
 * decides where to send the user on exactly those fields.
 *
 * A 401 here has already survived one silent refresh — the API middleware tries that before this
 * ever sees the failure — so it means the session is genuinely over.
 */
export function currentUserQuery() {
  return queryOptions({
    queryKey: sessionKeys.current(),
    queryFn: async ({ signal }): Promise<AuthenticatedUser> => {
      const { data, response } = await api.GET('/api/v1/auth/me', { signal });

      if (!response.ok || data === undefined) {
        throw new ApiError('Could not read the current session.', response.status);
      }

      return data;
    },
    // Signing in, signing out and changing a password are the only things that change the
    // answer, and all three invalidate it.
    staleTime: Infinity,
    retry: false,
  });
}
