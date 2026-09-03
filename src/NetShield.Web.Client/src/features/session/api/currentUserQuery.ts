import { queryOptions } from '@tanstack/react-query';

import { api } from '@/api/client';
import type { AuthenticatedUser } from '@/api/types';
import { sessionKeys } from '@/features/session/api/sessionKeys';

/**
 * Who the caller is, from `GET /api/v1/auth/me`.
 *
 * WP-0.6 wires the client and stops there — nothing renders this yet. The route guard, the
 * redirect a 401 causes and the forced password change all belong to WP-0.7, which is where the
 * answer starts changing what the user sees.
 */
export function currentUserQuery() {
  return queryOptions({
    queryKey: sessionKeys.current(),
    queryFn: async ({ signal }): Promise<AuthenticatedUser> => {
      const { data, response } = await api.GET('/api/v1/auth/me', { signal });

      if (!response.ok || data === undefined) {
        throw Object.assign(new Error('Could not read the current session.'), {
          status: response.status,
        });
      }

      return data;
    },
    // Signing in and out are the only things that change the answer, and both invalidate it.
    staleTime: Infinity,
    retry: false,
  });
}
