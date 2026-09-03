import { useRouter } from '@tanstack/react-router';

import { Button } from '@/components/ui/Button';
import { AuthLayout } from '@/features/auth/components/AuthLayout';

/**
 * What the guard shows when it could not find out who the caller is, for a reason that is not
 * "you are signed out" — the API is down, or answered something the client cannot read.
 *
 * DESIGN.md §8 asks an error to say what failed and what to do, and CONVENTIONS.md §6 asks it to
 * offer a retry. Without this the router falls back to its own bare "Something went wrong",
 * which does neither and shows the reader a raw parser message.
 *
 * The wording names the API rather than the error, because the reader can do nothing about a
 * parse failure and can do something about a service that has not started.
 */
export function SessionUnavailable() {
  const router = useRouter();

  return (
    <AuthLayout
      title="NetShield is not responding"
      subtitle="The console could not reach the NetShield API, so it cannot tell who you are."
    >
      <div className="space-y-4">
        <p className="text-body text-secondary">
          The API may still be starting. If it does not come back, check that the NetShield service
          is running and that this page is being served by it.
        </p>
        <Button
          fullWidth
          onClick={() => {
            void router.invalidate();
          }}
        >
          Try again
        </Button>
      </div>
    </AuthLayout>
  );
}
