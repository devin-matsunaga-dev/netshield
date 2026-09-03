import { createFileRoute } from '@tanstack/react-router';

import { PlaceholderPage } from '@/components/layout/PlaceholderPage';

export const Route = createFileRoute('/_app/policies')({
  component: () => (
    <PlaceholderPage
      title="Policies"
      subtitle="Alert rules, retention, notification routing, schedules and maintenance windows."
      arrivesIn="the policy work in Phase 8"
    />
  ),
});
