import { useMemo } from 'react';

import { navigation, type NavEntry } from '@/app/navigation';
import { usePermissions } from '@/features/session/hooks/usePermissions';

/**
 * The sidebar as this session should see it (WP-0.7).
 *
 * An entry the session does not hold the permission for is dropped, and a section left with no
 * children goes with them — a chevron over nothing is worse than an absence. The route itself is
 * not blocked: typing the address still reaches the screen, and the API still refuses whatever
 * that screen asks for. Hiding is a courtesy, and treating it as a boundary is how a client-side
 * check comes to be relied on.
 */
export function useVisibleNavigation(): readonly NavEntry[] {
  const holds = usePermissions();

  return useMemo(
    () =>
      navigation.flatMap((entry) => {
        if (entry.children === undefined) {
          return holds(entry.permission) ? [entry] : [];
        }

        const children = entry.children.filter((child) => holds(child.permission));

        return children.length > 0 ? [{ ...entry, children }] : [];
      }),
    [holds],
  );
}
