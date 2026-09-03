import { createFileRoute } from '@tanstack/react-router';

import { PlaceholderPage } from '@/components/layout/PlaceholderPage';

export const Route = createFileRoute('/administration/roles')({
  component: () => (
    <PlaceholderPage
      title="Roles"
      subtitle="What each role may do, and who holds it."
      arrivesIn="the administration work in Phase 8"
    />
  ),
});
