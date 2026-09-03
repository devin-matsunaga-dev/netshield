import { createFileRoute } from '@tanstack/react-router';

import { PlaceholderPage } from '@/components/layout/PlaceholderPage';

export const Route = createFileRoute('/logs')({
  component: () => (
    <PlaceholderPage
      title="Logs"
      subtitle="Search across normalised events, and the health of every source."
      arrivesIn="the log work in Phase 5"
    />
  ),
});
