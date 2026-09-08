import { useInfiniteQuery } from '@tanstack/react-query';
import { useVirtualizer } from '@tanstack/react-virtual';
import { useEffect, useRef } from 'react';

import { Badge, type BadgeTone } from '@/components/ui/Badge';
import { Card } from '@/components/ui/Card';
import { EmptyState } from '@/components/ui/EmptyState';
import { ErrorState } from '@/components/ui/ErrorState';
import { Skeleton } from '@/components/ui/Skeleton';
import type { Schemas } from '@/api/types';
import { deviceInterfacesQuery } from '@/features/devices/api/deviceQueries';
import { formatSpeed, interfaceStatusLabels } from '@/features/devices/components/deviceLabels';
import { toNumber } from '@/lib/apiNumber';

/**
 * How an interface status renders. `Up` is healthy, `Down` and `LowerLayerDown` are failures,
 * and everything else is neither — DESIGN.md §3 has no hue for "in between", so those sit on
 * `text-muted` rather than borrowing one that would mean something it does not.
 */
const statusTones: Record<Schemas['InterfaceStatus'], BadgeTone> = {
  Up: 'success',
  Down: 'danger',
  LowerLayerDown: 'danger',
  Testing: 'warning',
  Dormant: 'warning',
  NotPresent: 'muted',
  Unknown: 'muted',
};

const rowHeight = 44;

/**
 * A device's ports, as the last walk read them.
 *
 * Virtualized for the same reason the device table is: a stacked switch answers with over a
 * thousand rows, and DESIGN.md §9.6 renders no unbounded list past a hundred.
 *
 * An interface that has genuinely gone is not listed — `device_interfaces` is derived data and a
 * walk that read the whole table removes what it did not see, so this is the port list as of the
 * last complete walk rather than a history of every port there has ever been.
 */
export function DeviceInterfacesTab({ deviceId }: { readonly deviceId: string }) {
  const interfaces = useInfiniteQuery(deviceInterfacesQuery(deviceId));
  const scroller = useRef<HTMLDivElement>(null);

  const rows = interfaces.data?.pages.flatMap((page) => page.items) ?? [];

  const virtualizer = useVirtualizer({
    count: rows.length,
    getScrollElement: () => scroller.current,
    estimateSize: () => rowHeight,
    overscan: 12,
  });

  const items = virtualizer.getVirtualItems();
  const lastVisible = items.at(-1)?.index ?? 0;

  useEffect(() => {
    if (
      rows.length > 0 &&
      lastVisible >= rows.length - 10 &&
      interfaces.hasNextPage &&
      !interfaces.isFetchingNextPage
    ) {
      void interfaces.fetchNextPage();
    }
  }, [rows.length, lastVisible, interfaces]);

  if (interfaces.isPending) {
    return (
      <Card title="Interfaces">
        <div role="status" className="space-y-3" aria-busy="true" aria-label="Loading interfaces">
          {Array.from({ length: 6 }, (_, index) => (
            <Skeleton key={index} className="h-8 w-full" />
          ))}
        </div>
      </Card>
    );
  }

  if (interfaces.isError) {
    return (
      <Card title="Interfaces">
        <ErrorState
          title="The interface inventory could not be loaded."
          action="NetShield could not reach the API. Check that it is running and try again."
          onRetry={() => void interfaces.refetch()}
        />
      </Card>
    );
  }

  if (rows.length === 0) {
    return (
      <Card title="Interfaces">
        <EmptyState
          title="No interfaces recorded."
          action="Walk this device over SNMP to read its interface table."
        />
      </Card>
    );
  }

  return (
    <Card title={`Interfaces (${rows.length.toString()})`}>
      <div role="table" aria-label="Interfaces" className="flex flex-col">
        <div
          role="row"
          className="flex items-center gap-4 border-b border-subtle py-2 text-table-header text-muted"
        >
          <span role="columnheader" className="w-16 shrink-0">
            Index
          </span>
          <span role="columnheader" className="w-44 shrink-0">
            Name
          </span>
          <span role="columnheader" className="w-56 shrink-0">
            Description
          </span>
          <span role="columnheader" className="w-28 shrink-0">
            Admin
          </span>
          <span role="columnheader" className="w-36 shrink-0">
            Operational
          </span>
          <span role="columnheader" className="w-28 shrink-0">
            Speed
          </span>
          <span role="columnheader" className="w-44 shrink-0">
            MAC address
          </span>
        </div>

        <div ref={scroller} className="h-[50vh] overflow-auto" data-testid="interface-scroller">
          <div
            className="relative w-full"
            style={{ height: `${virtualizer.getTotalSize().toString()}px` }}
          >
            {items.map((item) => {
              const row = rows[item.index];

              if (row === undefined) {
                return null;
              }

              return (
                <div
                  key={row.id}
                  role="row"
                  className="absolute top-0 left-0 flex w-full items-center gap-4 border-b border-subtle text-table-cell text-secondary hover:bg-raised"
                  style={{
                    height: `${rowHeight.toString()}px`,
                    transform: `translateY(${item.start.toString()}px)`,
                  }}
                >
                  <span role="cell" className="w-16 shrink-0 tabular-nums">
                    {row.ifIndex}
                  </span>
                  <span role="cell" className="w-44 shrink-0 truncate font-mono text-primary">
                    {row.name ?? row.description ?? '—'}
                  </span>
                  <span role="cell" className="w-56 shrink-0 truncate">
                    {row.alias ?? row.description ?? '—'}
                  </span>
                  <span role="cell" className="w-28 shrink-0">
                    <Badge tone={statusTones[row.adminStatus]}>
                      {interfaceStatusLabels[row.adminStatus]}
                    </Badge>
                  </span>
                  <span role="cell" className="w-36 shrink-0">
                    <Badge tone={statusTones[row.operStatus]}>
                      {interfaceStatusLabels[row.operStatus]}
                    </Badge>
                  </span>
                  <span role="cell" className="w-28 shrink-0 tabular-nums">
                    {formatSpeed(toNumber(row.speedBitsPerSecond))}
                  </span>
                  <span role="cell" className="w-44 shrink-0 truncate font-mono">
                    {row.physicalAddress ?? '—'}
                  </span>
                </div>
              );
            })}
          </div>
        </div>
      </div>
    </Card>
  );
}
