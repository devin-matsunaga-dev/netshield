import { createFileRoute } from '@tanstack/react-router';

import { parseClientFilters, type ClientListFilters } from '@/features/clients/api/clientFilters';
import { ClientListPage } from '@/features/clients/components/ClientListPage';

/**
 * The client list. Its filters are the route's search parameters, which is what makes them
 * survive a refresh and travel in a link.
 *
 * The filters are parsed twice, deliberately, for the reason the device list's are: a TanStack
 * Router route inherits its parent's search parameters and merges its own over them, so a value
 * this route rejected is still present as the root parsed it. Sanitising at the point of use is
 * the settled answer (WP-0.7, WP-1.7) — without it, `?vlanId=melted` reaches the API and comes
 * back a 400.
 */
export const Route = createFileRoute('/_app/clients/')({
  validateSearch: (search: Record<string, unknown>): ClientListFilters =>
    parseClientFilters(search),
  component: function ClientList() {
    return <ClientListPage filters={parseClientFilters(Route.useSearch())} />;
  },
});
