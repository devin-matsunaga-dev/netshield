import { createFileRoute } from '@tanstack/react-router';

import { PlaceholderPage } from '@/components/layout/PlaceholderPage';

export const Route = createFileRoute('/_app/clients')({
  component: () => (
    <PlaceholderPage
      title="Clients"
      subtitle="Endpoints seen on the network, and where each one is attached."
      arrivesIn="the inventory work in Phase 1"
    />
  ),
});
