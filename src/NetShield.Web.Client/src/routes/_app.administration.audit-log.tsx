import { createFileRoute } from '@tanstack/react-router';

import { PlaceholderPage } from '@/components/layout/PlaceholderPage';

export const Route = createFileRoute('/_app/administration/audit-log')({
  component: () => (
    <PlaceholderPage
      title="Audit log"
      subtitle="Every state-changing call, with actor, target and outcome."
      arrivesIn="the administration work in Phase 8"
    />
  ),
});
