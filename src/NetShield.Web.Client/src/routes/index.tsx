import { createFileRoute, redirect } from '@tanstack/react-router';

/** The sidebar's first destination is the overview, so the root goes there. */
export const Route = createFileRoute('/')({
  beforeLoad: () => {
    throw redirect({ to: '/overview' });
  },
});
