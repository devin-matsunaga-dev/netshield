import { createFileRoute } from '@tanstack/react-router';

import { PlaceholderPage } from '@/components/layout/PlaceholderPage';

export const Route = createFileRoute('/_app/overview')({
  component: () => (
    <PlaceholderPage
      title="Network overview"
      subtitle="Real-time visibility and security status across your infrastructure."
      arrivesIn="the dashboard work in Phase 8"
    />
  ),
});
