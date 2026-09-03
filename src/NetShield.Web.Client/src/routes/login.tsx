import { createFileRoute, redirect } from '@tanstack/react-router';

import { LoginPage } from '@/features/auth/components/LoginPage';
import { safeReturnPath } from '@/features/auth/returnPath';
import { currentUserQuery } from '@/features/session/api/currentUserQuery';

export const Route = createFileRoute('/login')({
  validateSearch: (search: Record<string, unknown>): { redirect?: string } =>
    typeof search['redirect'] === 'string' ? { redirect: search['redirect'] } : {},
  beforeLoad: ({ context, search }) => {
    // Read from the cache; never fetch. The sign-in page makes exactly one request — the
    // sign-in. Asking `/auth/me` here would cost an anonymous visitor a refused refresh on
    // every page load, and WP-0.5 writes an audit row for each one: a log full of refresh
    // attempts nobody made. `_app` is what asks, and it puts the answer here.
    const user = context.queryClient.getQueryData(currentUserQuery().queryKey);

    if (user === undefined) {
      return;
    }

    throw redirect({
      to: user.mustChangePassword ? '/change-password' : safeReturnPath(search.redirect),
      replace: true,
    });
  },
  component: LoginPage,
});
