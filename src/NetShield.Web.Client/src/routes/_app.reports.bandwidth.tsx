import { createFileRoute } from '@tanstack/react-router';

import { PlaceholderPage } from '@/components/layout/PlaceholderPage';

export const Route = createFileRoute('/_app/reports/bandwidth')({
  component: () => (
    <PlaceholderPage
      title="Bandwidth report"
      subtitle="Utilisation, top talkers and top applications over a period."
      arrivesIn="the reporting work in Phase 8"
    />
  ),
});
