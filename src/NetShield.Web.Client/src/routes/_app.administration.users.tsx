import { createFileRoute } from '@tanstack/react-router';

import { PlaceholderPage } from '@/components/layout/PlaceholderPage';

export const Route = createFileRoute('/_app/administration/users')({
  component: () => (
    <PlaceholderPage
      title="Users"
      subtitle="Local accounts, their roles and their sign-in state."
      arrivesIn="the administration work in Phase 8"
    />
  ),
});
