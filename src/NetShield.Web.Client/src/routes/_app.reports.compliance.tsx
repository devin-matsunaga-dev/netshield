import { createFileRoute } from '@tanstack/react-router';

import { PlaceholderPage } from '@/components/layout/PlaceholderPage';

export const Route = createFileRoute('/_app/reports/compliance')({
  component: () => (
    <PlaceholderPage
      title="Compliance report"
      subtitle="Baseline pass and fail with the evidence behind each."
      arrivesIn="the reporting work in Phase 8"
    />
  ),
});
