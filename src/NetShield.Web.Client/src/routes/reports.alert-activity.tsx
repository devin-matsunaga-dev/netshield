import { createFileRoute } from '@tanstack/react-router';

import { PlaceholderPage } from '@/components/layout/PlaceholderPage';

export const Route = createFileRoute('/reports/alert-activity')({
  component: () => (
    <PlaceholderPage
      title="Alert activity report"
      subtitle="What alerted, how often, and how long it took to resolve."
      arrivesIn="the reporting work in Phase 8"
    />
  ),
});
