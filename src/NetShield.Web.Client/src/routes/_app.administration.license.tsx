import { createFileRoute } from '@tanstack/react-router';

import { PlaceholderPage } from '@/components/layout/PlaceholderPage';

export const Route = createFileRoute('/_app/administration/license')({
  component: () => (
    <PlaceholderPage
      title="License and version"
      subtitle="Which build is running, and under what licence."
      arrivesIn="the administration work in Phase 8"
    />
  ),
});
