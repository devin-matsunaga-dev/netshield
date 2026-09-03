import { createFileRoute, redirect } from '@tanstack/react-router';

/** Reports is a section rather than a destination; its first child is the inventory report. */
export const Route = createFileRoute('/reports/')({
  beforeLoad: () => {
    throw redirect({ to: '/reports/inventory' });
  },
});
