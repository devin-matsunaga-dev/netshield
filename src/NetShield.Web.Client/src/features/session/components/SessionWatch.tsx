import { useQueryClient } from '@tanstack/react-query';
import { useNavigate, useRouterState } from '@tanstack/react-router';
import { useEffect, useRef, type ReactNode } from 'react';

import { sessionExpiry } from '@/features/session/api/sessionExpiry';

interface SessionWatchProps {
  readonly children: ReactNode;
}

/**
 * Sends the user to sign in when a session ends mid-visit (WP-0.7).
 *
 * The `_app` guard catches a session that was already over when the page was opened. This is the
 * other half: a session that expires while the user is reading a screen. The API middleware has
 * by then tried one silent refresh and been refused, and announces it — this listens, empties the
 * cache so the next session cannot read the last one's data, and navigates.
 *
 * It acts on the first announcement only. A navigation is not instant, and any request still in
 * flight while it completes will announce again; without the latch each announcement would
 * restart the transition that produced the next one. The latch lives for as long as this
 * component does, which is until the sign-in page replaces the shell.
 */
export function SessionWatch({ children }: SessionWatchProps) {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const href = useRouterState({ select: (state) => state.location.href });
  const handled = useRef(false);

  useEffect(
    () =>
      sessionExpiry.subscribe(() => {
        if (handled.current) {
          return;
        }

        handled.current = true;
        queryClient.clear();

        void navigate({ to: '/login', search: { redirect: href }, replace: true });
      }),
    [navigate, queryClient, href],
  );

  return <>{children}</>;
}
