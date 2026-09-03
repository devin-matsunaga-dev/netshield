import { createFileRoute } from '@tanstack/react-router';

import { PlaceholderPage } from '@/components/layout/PlaceholderPage';

export const Route = createFileRoute('/_app/network')({
  component: () => (
    <PlaceholderPage
      title="Network"
      subtitle="The L2 and L3 topology built from neighbour, ARP and routing data."
      arrivesIn="the topology work in Phase 2"
    />
  ),
});
