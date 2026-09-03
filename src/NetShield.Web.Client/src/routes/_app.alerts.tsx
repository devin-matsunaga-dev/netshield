import { createFileRoute } from '@tanstack/react-router';

import { PlaceholderPage } from '@/components/layout/PlaceholderPage';

export const Route = createFileRoute('/_app/alerts')({
  component: () => (
    <PlaceholderPage
      title="Alerts"
      subtitle="Open incidents, their severity and who is on them."
      arrivesIn="the alerting work in Phase 6"
    />
  ),
});
