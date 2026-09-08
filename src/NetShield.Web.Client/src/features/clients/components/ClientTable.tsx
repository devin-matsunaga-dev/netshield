import { useNavigate } from '@tanstack/react-router';
import { useVirtualizer } from '@tanstack/react-virtual';
import { useEffect, useRef } from 'react';

import { RowMenu, RowMenuItem } from '@/components/ui/RowMenu';
import { Timestamp } from '@/components/ui/Timestamp';
import type { ClientSummary } from '@/features/clients/api/clientQueries';
import { formatOui } from '@/features/clients/components/clientLabels';
import { toNumber } from '@/lib/apiNumber';

interface ClientTableProps {
  readonly clients: readonly ClientSummary[];
  /** Ask for the next cursor page. Called as the reader nears the end of what is loaded. */
  readonly onEndReached: () => void;
  readonly loadingMore: boolean;
}

/** DESIGN.md §6: rows are 44px and the trailing row-menu column is 40px. */
const rowHeight = 44;

/** How close to the end is close enough to fetch the next page. */
const prefetchWithin = 10;

/**
 * The client table (DESIGN.md §6): a 12px `text-muted` header in sentence case over a hairline,
 * 44px rows each closed by a hairline, `bg-raised` on hover, a trailing 40px vertical-dots row
 * menu, and no zebra striping.
 *
 * Virtualized and built as an ARIA grid, for the reasons `DeviceTable` is: DESIGN.md §9.6 renders
 * no unbounded list past 100 rows, SPEC.md §1 targets 5,000 clients, and absolutely positioned
 * rows survive no real table layout — so the roles are what keep it a table to anything that is
 * not a pair of eyes.
 *
 * The MAC address is the primary column because it is the identity: an address is a lease and a
 * port is a cable, and the hardware address is the one thing that stays the same across both.
 */
export function ClientTable({ clients, onEndReached, loadingMore }: ClientTableProps) {
  const scroller = useRef<HTMLDivElement>(null);
  const navigate = useNavigate();

  const virtualizer = useVirtualizer({
    count: clients.length,
    getScrollElement: () => scroller.current,
    estimateSize: () => rowHeight,
    overscan: 12,
  });

  const rows = virtualizer.getVirtualItems();
  const lastVisible = rows.at(-1)?.index ?? 0;

  // In an effect rather than during render: asking for a page is a side effect, and React calls
  // a component's body more than once for reasons that have nothing to do with scrolling.
  useEffect(() => {
    if (clients.length > 0 && lastVisible >= clients.length - prefetchWithin) {
      onEndReached();
    }
  }, [clients.length, lastVisible, onEndReached]);

  return (
    <div role="table" aria-label="Clients" aria-rowcount={clients.length} className="flex flex-col">
      <div
        role="row"
        className="flex items-center gap-4 border-b border-subtle px-gutter py-2 text-table-header text-muted"
      >
        <span role="columnheader" className="w-44 shrink-0">
          MAC address
        </span>
        <span role="columnheader" className="w-40 shrink-0">
          Address
        </span>
        <span role="columnheader" className="w-36 shrink-0">
          Vendor prefix
        </span>
        <span role="columnheader" className="w-44 shrink-0">
          Attached to
        </span>
        <span role="columnheader" className="w-20 shrink-0">
          Port
        </span>
        <span role="columnheader" className="w-20 shrink-0">
          VLAN
        </span>
        <span role="columnheader" className="w-28 shrink-0">
          Last seen
        </span>
        <span role="columnheader" className="w-row-menu shrink-0">
          <span className="sr-only">Actions</span>
        </span>
      </div>

      <div ref={scroller} className="h-[60vh] overflow-auto" data-testid="client-scroller">
        <div
          className="relative w-full"
          style={{ height: `${virtualizer.getTotalSize().toString()}px` }}
        >
          {rows.map((row) => {
            const client = clients[row.index];

            if (client === undefined) {
              return null;
            }

            const ifIndex = toNumber(client.ifIndex);
            const vlanId = toNumber(client.vlanId);

            return (
              <div
                key={client.id}
                role="row"
                aria-rowindex={row.index + 1}
                className="absolute top-0 left-0 flex w-full items-center gap-4 border-b border-subtle px-gutter text-table-cell text-secondary transition-colors duration-hover hover:bg-raised"
                style={{
                  height: `${rowHeight.toString()}px`,
                  transform: `translateY(${row.start.toString()}px)`,
                }}
              >
                <span role="cell" className="w-44 shrink-0 truncate">
                  <a
                    href={`/clients/${client.id}`}
                    onClick={(event) => {
                      // Left click with no modifier navigates through the router; anything else
                      // is left to the browser, which is the point of this being a real anchor.
                      if (event.metaKey || event.ctrlKey || event.shiftKey || event.button !== 0) {
                        return;
                      }

                      event.preventDefault();
                      void navigate({ to: '/clients/$clientId', params: { clientId: client.id } });
                    }}
                    className="rounded-control font-mono text-primary hover:text-accent focus-visible:ring-2 focus-visible:ring-accent focus-visible:outline-none"
                  >
                    {client.macAddress}
                  </a>
                </span>
                <span role="cell" className="w-40 shrink-0 truncate font-mono tabular-nums">
                  {client.ipAddress ?? '—'}
                </span>
                <span role="cell" className="w-36 shrink-0 truncate font-mono">
                  {formatOui(client.oui, client.locallyAdministered)}
                </span>
                <span role="cell" className="w-44 shrink-0 truncate">
                  {client.deviceHostname ?? '—'}
                </span>
                <span role="cell" className="w-20 shrink-0 tabular-nums">
                  {ifIndex === null ? '—' : ifIndex.toString()}
                </span>
                <span role="cell" className="w-20 shrink-0 tabular-nums">
                  {vlanId === null ? '—' : vlanId.toString()}
                </span>
                <span role="cell" className="w-28 shrink-0 tabular-nums">
                  <Timestamp value={client.lastSeenAt} />
                </span>
                <span role="cell" className="w-row-menu shrink-0">
                  <RowMenu label={`Actions for ${client.macAddress}`}>
                    <RowMenuItem
                      onSelect={() =>
                        void navigate({
                          to: '/clients/$clientId',
                          params: { clientId: client.id },
                        })
                      }
                    >
                      Open client
                    </RowMenuItem>
                    {client.deviceId !== null && (
                      <RowMenuItem
                        onSelect={() =>
                          void navigate({
                            to: '/devices/$deviceId',
                            params: { deviceId: client.deviceId ?? '' },
                          })
                        }
                      >
                        Open device
                      </RowMenuItem>
                    )}
                  </RowMenu>
                </span>
              </div>
            );
          })}
        </div>
      </div>

      {loadingMore && (
        <p className="border-t border-subtle px-gutter py-2 text-metric-caption text-muted">
          Loading more clients…
        </p>
      )}
    </div>
  );
}
