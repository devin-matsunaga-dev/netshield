import { createFileRoute, Outlet, redirect } from '@tanstack/react-router';

import { AppShell } from '@/components/layout/AppShell';
import { SessionUnavailable } from '@/features/session/components/SessionUnavailable';
import { SessionWatch } from '@/features/session/components/SessionWatch';
import { ApiError, currentUserQuery } from '@/features/session/api/currentUserQuery';

/**
 * Everything behind a session (WP-0.7). Pathless, so the URLs below it are unchanged — `_app`
 * adds a guard and the chrome, not a segment.
 *
 * The guard runs before the shell renders rather than inside it, so an unauthenticated visitor
 * never sees a frame of the application they cannot use. `/login` and `/change-password` sit
 * outside this route for the same reason: the header would ask who the user is.
 */
export const Route = createFileRoute('/_app')({
  beforeLoad: async ({ context, location }) => {
    let user;

    try {
      // Resolved here rather than in a component, so that every page below the guard starts
      // with the answer in hand. A 401 arriving here has already survived one silent refresh.
      user = await context.queryClient.query({ ...currentUserQuery(), staleTime: 'static' });
    } catch (error) {
      if (error instanceof ApiError && error.status === 401) {
        // The path is carried so that signing in returns the user to what they asked for,
        // rather than to the overview and a second navigation.
        throw redirect({ to: '/login', search: { redirect: location.href } });
      }

      // Anything else — the API down, or an answer the client cannot read — is not a signed-out
      // session and must not be shown as one. It reaches `errorComponent` below.
      throw error;
    }

    if (user.mustChangePassword) {
      // WP-0.5 refuses this session everywhere except the change itself, `/auth/me` and
      // `/auth/logout`. Without this the user would meet a 403 on every screen and no
      // explanation of why.
      throw redirect({ to: '/change-password' });
    }
  },
  component: () => (
    <SessionWatch>
      <AppShell>
        <Outlet />
      </AppShell>
    </SessionWatch>
  ),
  errorComponent: SessionUnavailable,
});
