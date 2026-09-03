import { createFileRoute } from '@tanstack/react-router';

import { PlaceholderPage } from '@/components/layout/PlaceholderPage';

export const Route = createFileRoute('/_app/reports/inventory')({
  component: () => (
    <PlaceholderPage
      title="Inventory report"
      subtitle="What is on the network, as of a point in time."
      arrivesIn="the reporting work in Phase 8"
    />
  ),
});
