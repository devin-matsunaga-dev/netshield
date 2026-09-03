import { createFileRoute } from '@tanstack/react-router';

import { PlaceholderPage } from '@/components/layout/PlaceholderPage';

export const Route = createFileRoute('/vulnerabilities')({
  component: () => (
    <PlaceholderPage
      title="Vulnerabilities"
      subtitle="Imported scanner findings, correlated to the assets that carry them."
      arrivesIn="the vulnerability work in Phase 7"
    />
  ),
});
