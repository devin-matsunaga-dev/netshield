import { createFileRoute } from '@tanstack/react-router';

import { RunDetailPage } from '@/features/discovery/components/RunDetailPage';

/** One discovery run: what was swept, what answered, and what each answer turned out to be. */
export const Route = createFileRoute('/_app/devices/discovery/runs/$runId')({
  component: function RunDetail() {
    return <RunDetailPage runId={Route.useParams().runId} />;
  },
});
