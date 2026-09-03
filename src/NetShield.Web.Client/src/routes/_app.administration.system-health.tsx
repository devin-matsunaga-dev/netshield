import { createFileRoute } from '@tanstack/react-router';

import { PlaceholderPage } from '@/components/layout/PlaceholderPage';

export const Route = createFileRoute('/_app/administration/system-health')({
  component: () => (
    <PlaceholderPage
      title="System health"
      subtitle="The state of each NetShield process, store and collector."
      arrivesIn="the administration work in Phase 8"
    />
  ),
});
