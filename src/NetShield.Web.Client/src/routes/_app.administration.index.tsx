import { createFileRoute, redirect } from '@tanstack/react-router';

/** Administration is a section rather than a destination; its first child is the user list. */
export const Route = createFileRoute('/_app/administration/')({
  beforeLoad: () => {
    throw redirect({ to: '/administration/users' });
  },
});
