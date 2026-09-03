import { createFileRoute } from '@tanstack/react-router';

import { PlaceholderPage } from '@/components/layout/PlaceholderPage';

export const Route = createFileRoute('/reports/availability')({
  component: () => (
    <PlaceholderPage
      title="Availability report"
      subtitle="Uptime and reachability over a period."
      arrivesIn="the reporting work in Phase 8"
    />
  ),
});
