import { createFileRoute } from '@tanstack/react-router';

import { PlaceholderPage } from '@/components/layout/PlaceholderPage';

export const Route = createFileRoute('/compliance')({
  component: () => (
    <PlaceholderPage
      title="Compliance"
      subtitle="Baseline assessment results, per device and per baseline."
      arrivesIn="the compliance work in Phase 7"
    />
  ),
});
