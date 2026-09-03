import { createFileRoute, redirect } from '@tanstack/react-router';

import { ChangePasswordPage } from '@/features/auth/components/ChangePasswordPage';
import { ApiError, currentUserQuery } from '@/features/session/api/currentUserQuery';

/**
 * The forced password change. Outside `_app`, because `_app` is what sends people here — a
 * screen that lived under that guard would redirect to itself for ever.
 */
export const Route = createFileRoute('/change-password')({
  beforeLoad: async ({ context, location }) => {
    let user;

    try {
      user = await context.queryClient.query({ ...currentUserQuery(), staleTime: 'static' });
    } catch (error) {
      if (error instanceof ApiError && error.status === 401) {
        throw redirect({ to: '/login', search: { redirect: location.href } });
      }

      throw error;
    }

    if (!user.mustChangePassword) {
      // Nobody reaches this screen by choice. Changing a password when it is not owed is a
      // profile action, and belongs to the Administration work in Phase 8.
      throw redirect({ to: '/overview', replace: true });
    }
  },
  component: ChangePasswordPage,
});
