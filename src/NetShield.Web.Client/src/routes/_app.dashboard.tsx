import { createFileRoute } from '@tanstack/react-router';

import { PlaceholderPage } from '@/components/layout/PlaceholderPage';

export const Route = createFileRoute('/_app/dashboard')({
  component: () => (
    <PlaceholderPage
      title="Dashboard"
      subtitle="Your own arrangement of the widget catalogue."
      arrivesIn="the dashboard work in Phase 8"
    />
  ),
});
