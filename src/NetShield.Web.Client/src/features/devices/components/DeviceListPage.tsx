import { useInfiniteQuery } from '@tanstack/react-query';
import { Link, useNavigate } from '@tanstack/react-router';
import { useCallback } from 'react';

import { PageHeader } from '@/components/layout/PageHeader';
import { Button } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';
import { EmptyState } from '@/components/ui/EmptyState';
import { ErrorState } from '@/components/ui/ErrorState';
import { Skeleton } from '@/components/ui/Skeleton';
import { hasActiveFilter, type DeviceListFilters } from '@/features/devices/api/deviceFilters';
import { toNumber } from '@/lib/apiNumber';
import { deviceListQuery } from '@/features/devices/api/deviceQueries';
import {
  DeviceFilters,
  type DeviceFilterChange,
} from '@/features/devices/components/DeviceFilters';
import { DeviceTable } from '@/features/devices/components/DeviceTable';
import { RequirePermission } from '@/features/session/components/RequirePermission';

interface DeviceListPageProps {
  readonly filters: DeviceListFilters;
}

/**
 * The devices screen: the estate, narrowed.
 *
 * The filters live in the URL and nowhere else. That is what makes them survive a refresh, work
 * with the back button and travel in a pasted link — and it means there is exactly one place
 * that says what the list is currently showing, so the query key, the table and the address bar
 * cannot disagree.
 */
export function DeviceListPage({ filters }: DeviceListPageProps) {
  const navigate = useNavigate();

  const devices = useInfiniteQuery(deviceListQuery(filters));

  const applyFilter = useCallback(
    (change: DeviceFilterChange) => {
      void navigate({
        to: '/devices',
        // Merged onto what is there, so each control changes its own field and leaves the rest.
        search: (current) => ({ ...current, ...change }),
        replace: true,
      });
    },
    [navigate],
  );

  const clearFilters = useCallback(() => {
    void navigate({ to: '/devices', search: {}, replace: true });
  }, [navigate]);

  const loadMore = useCallback(() => {
    if (devices.hasNextPage && !devices.isFetchingNextPage) {
      void devices.fetchNextPage();
    }
  }, [devices]);

  const rows = devices.data?.pages.flatMap((page) => page.items) ?? [];
  // Coerced, because the contract types every number as `number | string` — so `total === 1`
  // would be false for a single device and the subtitle would read "1 devices".
  const total = toNumber(devices.data?.pages[0]?.totalCount);

  return (
    <>
      <div className="mb-gutter flex items-start justify-between gap-4">
        <PageHeader
          title="Devices"
          subtitle={
            total === null
              ? 'Every monitored device, its fingerprint and its state.'
              : `${total.toString()} ${total === 1 ? 'device' : 'devices'} in the inventory.`
          }
        />
        <div className="flex gap-2">
          <Link to="/devices/discovery">
            <Button variant="secondary">Discovery</Button>
          </Link>
          {/*
            Behind CredentialsManage, which is Administrator-only: WP-1.2 settled that even the
            list of profile names is, because it says which accounts NetShield holds passwords
            for. The API refuses the routes regardless — hiding is presentation.
          */}
          <RequirePermission permission="CredentialsManage">
            <Link to="/devices/credentials">
              <Button variant="secondary">Credentials</Button>
            </Link>
          </RequirePermission>
          {/*
            Hidden for a session that cannot write, and refused by the API either way — hiding is
            presentation and never the boundary (ARCHITECTURE.md §8).
          */}
          <RequirePermission permission="InventoryWrite">
            <Link to="/devices/new">
              <Button>Add device</Button>
            </Link>
          </RequirePermission>
        </div>
      </div>

      <div className="mb-gutter">
        <Card>
          <DeviceFilters filters={filters} onChange={applyFilter} onClear={clearFilters} />
        </Card>
      </div>

      <Card>
        {devices.isPending ? (
          // A skeleton matching the final layout, not a centred spinner (CONVENTIONS.md §6).
          <div role="status" className="space-y-3" aria-busy="true" aria-label="Loading devices">
            {Array.from({ length: 8 }, (_, index) => (
              <Skeleton key={index} className="h-8 w-full" />
            ))}
          </div>
        ) : devices.isError ? (
          <ErrorState
            title="The device list could not be loaded."
            action="NetShield could not reach the API. Check that it is running and try again."
            onRetry={() => void devices.refetch()}
          />
        ) : rows.length === 0 ? (
          hasActiveFilter(filters) ? (
            <EmptyState
              title="No devices match these filters."
              action="Widen or clear the filters to see the rest of the inventory."
            >
              <Button variant="secondary" onClick={clearFilters}>
                Clear filters
              </Button>
            </EmptyState>
          ) : (
            <EmptyState title="No devices yet." action="Run discovery or add one manually.">
              <RequirePermission permission="InventoryWrite">
                <Link to="/devices/new">
                  <Button>Add device</Button>
                </Link>
              </RequirePermission>
            </EmptyState>
          )
        ) : (
          <DeviceTable
            devices={rows}
            onEndReached={loadMore}
            loadingMore={devices.isFetchingNextPage}
          />
        )}
      </Card>
    </>
  );
}
