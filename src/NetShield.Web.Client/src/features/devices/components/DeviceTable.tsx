import { useNavigate } from '@tanstack/react-router';
import { useVirtualizer } from '@tanstack/react-virtual';
import { useEffect, useRef } from 'react';

import { RowMenu, RowMenuItem } from '@/components/ui/RowMenu';
import { Timestamp } from '@/components/ui/Timestamp';
import type { DeviceSummary } from '@/features/devices/api/deviceQueries';
import { DeviceStateBadge } from '@/features/devices/components/DeviceStateBadge';
import {
  criticalityLabels,
  roleLabels,
  vendorLabels,
} from '@/features/devices/components/deviceLabels';

interface DeviceTableProps {
  readonly devices: readonly DeviceSummary[];
  /** Ask for the next cursor page. Called as the reader nears the end of what is loaded. */
  readonly onEndReached: () => void;
  readonly loadingMore: boolean;
}

/** DESIGN.md §6: rows are 44px and the trailing row-menu column is 40px. */
const rowHeight = 44;

/** How close to the end is close enough to fetch the next page. */
const prefetchWithin = 10;

/**
 * The device table (DESIGN.md §6): a 12px `text-muted` header in sentence case over a hairline,
 * 44px rows each closed by a hairline, `bg-raised` on hover, a trailing 40px vertical-dots row
 * menu, and no zebra striping.
 *
 * Virtualized — DESIGN.md §9.6 renders no unbounded list past 100 rows, and WP-1.7 has to carry
 * 500 devices smoothly. Only the rows in view are in the DOM; the scroll container holds the
 * full height, so the scrollbar still tells the truth about how much there is.
 *
 * An ARIA grid rather than a `<table>`: the rows are absolutely positioned, which no real table
 * layout survives. The roles are what keep it a table to anything that is not a pair of eyes,
 * and the hostname cell holds a real link so the row is reachable, focusable and openable in a
 * new tab like any other.
 */
export function DeviceTable({ devices, onEndReached, loadingMore }: DeviceTableProps) {
  const scroller = useRef<HTMLDivElement>(null);
  const navigate = useNavigate();

  const virtualizer = useVirtualizer({
    count: devices.length,
    getScrollElement: () => scroller.current,
    estimateSize: () => rowHeight,
    // Enough rows either side that a fast scroll does not reach empty space before React does.
    overscan: 12,
  });

  const rows = virtualizer.getVirtualItems();
  const lastVisible = rows.at(-1)?.index ?? 0;

  // In an effect rather than during render: asking for a page is a side effect, and React calls
  // a component's body more than once for reasons that have nothing to do with scrolling.
  useEffect(() => {
    if (devices.length > 0 && lastVisible >= devices.length - prefetchWithin) {
      onEndReached();
    }
  }, [devices.length, lastVisible, onEndReached]);

  return (
    <div role="table" aria-label="Devices" aria-rowcount={devices.length} className="flex flex-col">
      <div
        role="row"
        className="flex items-center gap-4 border-b border-subtle px-gutter py-2 text-table-header text-muted"
      >
        <span role="columnheader" className="w-56 shrink-0">
          Hostname
        </span>
        <span role="columnheader" className="w-40 shrink-0">
          Address
        </span>
        <span role="columnheader" className="w-24 shrink-0">
          State
        </span>
        <span role="columnheader" className="w-36 shrink-0">
          Vendor
        </span>
        <span role="columnheader" className="w-28 shrink-0">
          Role
        </span>
        <span role="columnheader" className="w-32 shrink-0">
          Site
        </span>
        <span role="columnheader" className="w-24 shrink-0">
          Criticality
        </span>
        <span role="columnheader" className="w-28 shrink-0">
          Updated
        </span>
        <span role="columnheader" className="w-row-menu shrink-0">
          <span className="sr-only">Actions</span>
        </span>
      </div>

      <div
        ref={scroller}
        // A fixed viewport is what makes virtualization mean anything: the scroll happens here
        // rather than on the page, so the window of rows is computable.
        className="h-[60vh] overflow-auto"
        data-testid="device-scroller"
      >
        <div
          className="relative w-full"
          style={{ height: `${virtualizer.getTotalSize().toString()}px` }}
        >
          {rows.map((row) => {
            const device = devices[row.index];

            if (device === undefined) {
              return null;
            }

            return (
              <div
                key={device.id}
                role="row"
                aria-rowindex={row.index + 1}
                className="absolute top-0 left-0 flex w-full items-center gap-4 border-b border-subtle px-gutter text-table-cell text-secondary transition-colors duration-hover hover:bg-raised"
                style={{
                  height: `${rowHeight.toString()}px`,
                  transform: `translateY(${row.start.toString()}px)`,
                }}
              >
                <span role="cell" className="w-56 shrink-0 truncate">
                  <a
                    href={`/devices/${device.id}`}
                    onClick={(event) => {
                      // Left click with no modifier navigates through the router; anything else
                      // — middle click, ⌘-click — is left to the browser, which is the point of
                      // this being a real anchor.
                      if (event.metaKey || event.ctrlKey || event.shiftKey || event.button !== 0) {
                        return;
                      }

                      event.preventDefault();
                      void navigate({ to: '/devices/$deviceId', params: { deviceId: device.id } });
                    }}
                    className="rounded-control text-primary hover:text-accent focus-visible:ring-2 focus-visible:ring-accent focus-visible:outline-none"
                  >
                    {device.hostname}
                  </a>
                </span>
                <span role="cell" className="w-40 shrink-0 truncate font-mono tabular-nums">
                  {device.primaryIpAddress}
                </span>
                <span role="cell" className="w-24 shrink-0">
                  <DeviceStateBadge state={device.state} />
                </span>
                <span role="cell" className="w-36 shrink-0 truncate">
                  {vendorLabels[device.vendor]}
                </span>
                <span role="cell" className="w-28 shrink-0 truncate">
                  {roleLabels[device.role]}
                </span>
                <span role="cell" className="w-32 shrink-0 truncate">
                  {device.site ?? '—'}
                </span>
                <span role="cell" className="w-24 shrink-0">
                  {criticalityLabels[device.criticality]}
                </span>
                <span role="cell" className="w-28 shrink-0 tabular-nums">
                  <Timestamp value={device.updatedAt} />
                </span>
                <span role="cell" className="w-row-menu shrink-0">
                  <RowMenu label={`Actions for ${device.hostname}`}>
                    <RowMenuItem
                      onSelect={() =>
                        void navigate({
                          to: '/devices/$deviceId',
                          params: { deviceId: device.id },
                        })
                      }
                    >
                      Open device
                    </RowMenuItem>
                    <RowMenuItem
                      onSelect={() =>
                        void navigate({
                          to: '/devices/$deviceId',
                          params: { deviceId: device.id },
                          search: { tab: 'settings' },
                        })
                      }
                    >
                      Edit device
                    </RowMenuItem>
                  </RowMenu>
                </span>
              </div>
            );
          })}
        </div>
      </div>

      {loadingMore && (
        <p className="border-t border-subtle px-gutter py-2 text-metric-caption text-muted">
          Loading more devices…
        </p>
      )}
    </div>
  );
}
