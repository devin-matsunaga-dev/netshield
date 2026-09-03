import type { Middleware } from 'openapi-fetch';

import { sessionExpiry } from '@/features/session/api/sessionExpiry';

/** The session cookie is short-lived; this is the endpoint that mints a new one. */
export const refreshPath = '/api/v1/auth/refresh';

/**
 * The two paths a 401 is an answer rather than an expiry. Signing in with a wrong password and
 * failing to refresh both return 401, and retrying either would be a loop.
 */
const notRecoverable = new Set(['/api/v1/auth/login', refreshPath]);

/**
 * In flight, or absent. One refresh serves every request that hit a 401 while it was running —
 * the refresh token rotates on use, so a second concurrent call would present a token the first
 * one had already spent, and WP-0.4 revokes the whole chain when that happens. Racing ourselves
 * would sign the user out.
 */
let inFlight: Promise<boolean> | null = null;

/**
 * Turns an expired session into a new one, once (WP-0.7).
 *
 * The session cookie lasts fifteen minutes and the refresh cookie fourteen days, so an idle
 * operator's next click is the request that finds the session gone. This retries that request
 * behind a single refresh; if the refresh is refused, the session is over and the guard is told.
 */
export function silentRefresh(
  fetcher: typeof globalThis.fetch = (request) => globalThis.fetch(request),
): Middleware {
  return {
    async onResponse({ request, response }) {
      if (response.status !== 401 || notRecoverable.has(new URL(request.url).pathname)) {
        return undefined;
      }

      // The body has not been read at this point, so the request can still be replayed. It is
      // cloned before the refresh rather than after, because `request` is consumed by the retry.
      const retry = request.clone();

      if (!(await refreshOnce(fetcher, request.url))) {
        sessionExpiry.announce();

        return undefined;
      }

      return fetcher(retry);
    },
  };
}

/** Resets the single-flight latch. For tests, which run many sessions in one process. */
export function resetSilentRefresh(): void {
  inFlight = null;
}

async function refreshOnce(fetcher: typeof globalThis.fetch, from: string): Promise<boolean> {
  inFlight ??= attempt(fetcher, from).finally(() => {
    inFlight = null;
  });

  return inFlight;
}

async function attempt(fetcher: typeof globalThis.fetch, from: string): Promise<boolean> {
  try {
    const response = await fetcher(
      new Request(new URL(refreshPath, from), {
        method: 'POST',
        credentials: 'same-origin',
      }),
    );

    return response.ok;
  } catch {
    // A refresh that could not be sent at all is not an expired session — it is an unreachable
    // API — but the caller's request has already failed and there is nothing to retry it with.
    return false;
  }
}
