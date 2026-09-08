import { useInfiniteQuery } from '@tanstack/react-query';
import { useNavigate } from '@tanstack/react-router';
import { useCallback } from 'react';

import { PageHeader } from '@/components/layout/PageHeader';
import { Button } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';
import { EmptyState } from '@/components/ui/EmptyState';
import { ErrorState } from '@/components/ui/ErrorState';
import { Skeleton } from '@/components/ui/Skeleton';
import { hasActiveFilter, type ClientListFilters } from '@/features/clients/api/clientFilters';
import { clientListQuery } from '@/features/clients/api/clientQueries';
import {
  ClientFilters,
  type ClientFilterChange,
} from '@/features/clients/components/ClientFilters';
import { ClientTable } from '@/features/clients/components/ClientTable';
import { ResolvePanel } from '@/features/clients/components/ResolvePanel';
import { toNumber } from '@/lib/apiNumber';

interface ClientListPageProps {
  readonly filters: ClientListFilters;
}

/**
 * The clients screen: every endpoint NetShield has seen, and where each one is attached.
 *
 * The filters live in the URL and nowhere else, the same arrangement the device list uses and
 * for the same reasons — they survive a refresh, work with the back button and travel in a
 * pasted link, and there is exactly one place saying what the list is showing.
 *
 * There is no "add client" and there is not meant to be. A client is something NetShield
 * observed rather than something an operator maintains, and a hand-entered one would be a claim
 * about the network with no evidence behind it. What the screen offers instead is the resolution
 * panel: the way to check what an address meant at a moment.
 */
export function ClientListPage({ filters }: ClientListPageProps) {
  const navigate = useNavigate();

  const clients = useInfiniteQuery(clientListQuery(filters));

  const applyFilter = useCallback(
    (change: ClientFilterChange) => {
      void navigate({
        to: '/clients',
        // Merged onto what is there, so each control changes its own field and leaves the rest.
        search: (current) => ({ ...current, ...change }),
        replace: true,
      });
    },
    [navigate],
  );

  const clearFilters = useCallback(() => {
    void navigate({ to: '/clients', search: {}, replace: true });
  }, [navigate]);

  const loadMore = useCallback(() => {
    if (clients.hasNextPage && !clients.isFetchingNextPage) {
      void clients.fetchNextPage();
    }
  }, [clients]);

  const rows = clients.data?.pages.flatMap((page) => page.items) ?? [];
  // Coerced, because the contract types every number as `number | string` — so `total === 1`
  // would be false for a single client and the subtitle would read "1 clients".
  const total = toNumber(clients.data?.pages[0]?.totalCount);

  return (
    <>
      <PageHeader
        title="Clients"
        subtitle={
          total === null
            ? 'Endpoints seen on the network, and where each one is attached.'
            : `${total.toString()} ${total === 1 ? 'client' : 'clients'} seen on the network.`
        }
      />

      <div className="mb-gutter">
        <ResolvePanel />
      </div>

      <div className="mb-gutter">
        <Card>
          <ClientFilters filters={filters} onChange={applyFilter} onClear={clearFilters} />
        </Card>
      </div>

      <Card>
        {clients.isPending ? (
          // A skeleton matching the final layout, not a centred spinner (CONVENTIONS.md §6).
          <div role="status" className="space-y-3" aria-busy="true" aria-label="Loading clients">
            {Array.from({ length: 8 }, (_, index) => (
              <Skeleton key={index} className="h-8 w-full" />
            ))}
          </div>
        ) : clients.isError ? (
          <ErrorState
            title="The client list could not be loaded."
            action="NetShield could not reach the API. Check that it is running and try again."
            onRetry={() => void clients.refetch()}
          />
        ) : rows.length === 0 ? (
          hasActiveFilter(filters) ? (
            <EmptyState
              title="No clients match these filters."
              action="Widen or clear the filters to see the rest of what has been seen."
            >
              <Button variant="secondary" onClick={clearFilters}>
                Clear filters
              </Button>
            </EmptyState>
          ) : (
            <EmptyState
              title="No clients yet."
              action="Clients appear once a device's ARP or MAC address table has been read. Add a device with an SNMP credential profile, or read one now from its own page."
            />
          )
        ) : (
          <ClientTable
            clients={rows}
            onEndReached={loadMore}
            loadingMore={clients.isFetchingNextPage}
          />
        )}
      </Card>
    </>
  );
}
