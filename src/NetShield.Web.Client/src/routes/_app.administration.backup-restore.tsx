import { createFileRoute } from '@tanstack/react-router';

import { PlaceholderPage } from '@/components/layout/PlaceholderPage';

export const Route = createFileRoute('/_app/administration/backup-restore')({
  component: () => (
    <PlaceholderPage
      title="Backup and restore"
      subtitle="Platform configuration backups, and restoring from one."
      arrivesIn="the administration work in Phase 8"
    />
  ),
});
