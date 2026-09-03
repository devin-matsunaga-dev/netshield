import { createFileRoute } from '@tanstack/react-router';

import { PlaceholderPage } from '@/components/layout/PlaceholderPage';

export const Route = createFileRoute('/_app/threats')({
  component: () => (
    <PlaceholderPage
      title="Threats"
      subtitle="What the log and flow rules have flagged as hostile."
      arrivesIn="the alerting work in Phase 6"
    />
  ),
});
