import { createFileRoute } from '@tanstack/react-router';

import { ClientDetailPage } from '@/features/clients/components/ClientDetailPage';
import { clientTabs, type ClientTab } from '@/features/clients/components/clientTabs';

/**
 * One client. The active tab is a search parameter, so a tab is a place that can be linked to,
 * refreshed and arrived at from the list's row menu.
 *
 * An unrecognised `tab` falls back to the overview rather than rendering nothing, for the same
 * reason the list drops a filter it does not know: a hand-edited address should still arrive
 * somewhere. Absent is also the overview, so the plain client URL carries no search parameter.
 */
export const Route = createFileRoute('/_app/clients/$clientId')({
  validateSearch: (search: Record<string, unknown>): { tab?: ClientTab } =>
    typeof search['tab'] === 'string' && (clientTabs as readonly string[]).includes(search['tab'])
      ? { tab: search['tab'] as ClientTab }
      : {},
  component: function ClientDetail() {
    return (
      <ClientDetailPage
        clientId={Route.useParams().clientId}
        tab={Route.useSearch().tab ?? 'overview'}
      />
    );
  },
});
