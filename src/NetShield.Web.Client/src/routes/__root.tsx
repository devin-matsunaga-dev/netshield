import { createRootRouteWithContext, Outlet } from '@tanstack/react-router';

import type { RouterContext } from '@/app/router';
import { NotFoundPage } from '@/components/layout/NotFoundPage';

/**
 * The root of the tree. It renders nothing of its own: the chrome belongs to `_app`, which is
 * also where the session guard lives, so that sign-in and the forced password change can render
 * outside a shell whose header would ask who the user is.
 */
export const Route = createRootRouteWithContext<RouterContext>()({
  component: Outlet,
  notFoundComponent: NotFoundPage,
});
