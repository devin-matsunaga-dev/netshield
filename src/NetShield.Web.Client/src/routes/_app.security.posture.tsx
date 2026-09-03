import { createFileRoute } from '@tanstack/react-router';

import { PlaceholderPage } from '@/components/layout/PlaceholderPage';

export const Route = createFileRoute('/_app/security/posture')({
  component: () => (
    <PlaceholderPage
      title="Security posture"
      subtitle="How the estate scores against the checks that apply to it."
      arrivesIn="the compliance work in Phase 7"
    />
  ),
});
