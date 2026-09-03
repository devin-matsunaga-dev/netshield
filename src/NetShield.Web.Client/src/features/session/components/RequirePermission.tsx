import type { ReactNode } from 'react';

import type { Permission } from '@/api/types';
import { usePermissions } from '@/features/session/hooks/usePermissions';

interface RequirePermissionProps {
  /** What the session has to hold for `children` to be drawn. */
  readonly permission: Permission;
  readonly children: ReactNode;
  /** What to draw instead. Omitted means nothing at all, which is the usual answer. */
  readonly fallback?: ReactNode;
}

/**
 * Draws `children` only for a session that holds `permission` (WP-0.7).
 *
 * This is presentation. The API refuses the same call whether or not the button that would have
 * made it was rendered, and nothing here should ever be the only check — ARCHITECTURE.md §8
 * checks at the endpoint and again in the module. Wrapping a write control in it spares a
 * read-only user a 403 they could not have predicted.
 */
export function RequirePermission({ permission, children, fallback }: RequirePermissionProps) {
  const holds = usePermissions();

  return <>{holds(permission) ? children : fallback}</>;
}
