import { createFileRoute, redirect } from '@tanstack/react-router';

/** Security is a section rather than a destination; its first child is the posture screen. */
export const Route = createFileRoute('/security/')({
  beforeLoad: () => {
    throw redirect({ to: '/security/posture' });
  },
});
