import { useQuery } from '@tanstack/react-query';

import type { AuthenticatedUser } from '@/api/types';
import { currentUserQuery } from '@/features/session/api/currentUserQuery';

/**
 * The signed-in user, inside the guarded part of the route tree.
 *
 * The `_app` guard has already resolved this query and redirected if it failed, so under normal
 * rendering there is always a session here. It is still nullable, and deliberately: signing out
 * empties the cache while the shell is briefly still mounted, and there is exactly one honest
 * thing to draw in that moment — nothing. A hook that promised a user would have to either
 * suspend with no boundary above it or assert its way past the gap, and both of those turn a
 * one-frame absence into a render loop.
 */
export function useSession(): AuthenticatedUser | undefined {
  const { data } = useQuery(currentUserQuery());

  return data;
}
