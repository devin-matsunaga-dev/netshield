import { createFileRoute } from '@tanstack/react-router';

import { PlaceholderPage } from '@/components/layout/PlaceholderPage';

export const Route = createFileRoute('/devices')({
  component: () => (
    <PlaceholderPage
      title="Devices"
      subtitle="Every monitored device, its fingerprint and its state."
      arrivesIn="the inventory work in Phase 1"
    />
  ),
});
