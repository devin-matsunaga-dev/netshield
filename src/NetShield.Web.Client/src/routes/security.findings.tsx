import { createFileRoute } from '@tanstack/react-router';

import { PlaceholderPage } from '@/components/layout/PlaceholderPage';

export const Route = createFileRoute('/security/findings')({
  component: () => (
    <PlaceholderPage
      title="Security findings"
      subtitle="What the assessments and imports have turned up."
      arrivesIn="the compliance work in Phase 7"
    />
  ),
});
