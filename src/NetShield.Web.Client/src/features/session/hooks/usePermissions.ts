import { useCallback } from 'react';

import type { Permission } from '@/api/types';
import { useSession } from '@/features/session/hooks/useSession';

/**
 * Asks what the session may do, for the purpose of deciding what to draw.
 *
 * The list comes from the server, which resolved it from the role and will resolve it again on
 * every protected request. Hiding a control the user does not hold is a courtesy — it keeps them
 * from finding out by being refused — and never the thing that stops them.
 *
 * With no session it holds nothing, so a shell caught mid-sign-out draws no destination it would
 * have to take back. Failing closed is free here: the API is what actually refuses.
 */
export function usePermissions(): (permission: Permission | undefined) => boolean {
  const user = useSession();
  const permissions = user?.permissions;

  return useCallback(
    (permission) => {
      if (permissions === undefined) {
        return false;
      }

      // An entry that names no permission needs none: a session is enough to see it.
      return permission === undefined || permissions.includes(permission);
    },
    [permissions],
  );
}
