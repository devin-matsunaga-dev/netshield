import { useNavigate } from '@tanstack/react-router';
import { useVirtualizer } from '@tanstack/react-virtual';
import { useRef } from 'react';

import type { TopologyNode } from '@/features/topology/api/topologyGraph';
import { roleLabels, stateLabels, stateTones } from '@/features/topology/components/topologyLabels';
import { cn } from '@/lib/cn';

/** DESIGN.md §6: rows are 44px. */
const rowHeight = 44;

interface TopologyDeviceTableProps {
  readonly nodes: readonly TopologyNode[];
}

/**
 * Half of the map's table fallback (DESIGN.md §9.7): every device on it, in the graph's own
 * ordering — component, then rank within the component, then device id.
 *
 * That ordering is the point rather than an accident of the transport. A device list sorted by
 * hostname would be the Devices screen again; this one reads the estate outwards from its core
 * and finishes on the islands, which is the shape the canvas draws and the only thing a table
 * can say about a picture.
 *
 * Virtualized, because the target estate is 500 devices and DESIGN.md §9.6 renders no unbounded
 * list past 100 rows. An ARIA grid rather than a `<table>` for the reason WP-1.7's device table
 * is one: the rows are absolutely positioned, which no real table layout survives.
 */
export function TopologyDeviceTable({ nodes }: TopologyDeviceTableProps) {
  const scroller = useRef<HTMLDivElement>(null);
  const navigate = useNavigate();

  const virtualizer = useVirtualizer({
    count: nodes.length,
    getScrollElement: () => scroller.current,
    estimateSize: () => rowHeight,
    overscan: 12,
  });

  return (
    <div
      role="table"
      aria-label="Devices on the map"
      aria-rowcount={nodes.length}
      className="flex flex-col"
    >
      <div
        role="row"
        className="flex items-center gap-4 border-b border-subtle px-gutter py-2 text-table-header text-muted"
      >
        <span role="columnheader" className="w-56 shrink-0">
          Device
        </span>
        <span role="columnheader" className="w-24 shrink-0">
          State
        </span>
        <span role="columnheader" className="w-28 shrink-0">
          Role
        </span>
        <span role="columnheader" className="w-32 shrink-0">
          Site
        </span>
        <span role="columnheader" className="w-20 shrink-0">
          Group
        </span>
        <span role="columnheader" className="w-20 shrink-0">
          Hops
        </span>
        <span role="columnheader" className="w-20 shrink-0">
          Links
        </span>
        <span role="columnheader" className="w-28 shrink-0">
          Not drawn
        </span>
      </div>

      <div ref={scroller} className="max-h-[60vh] overflow-auto" data-testid="topology-devices">
        <div
          className="relative w-full"
          style={{ height: `${virtualizer.getTotalSize().toString()}px` }}
        >
          {virtualizer.getVirtualItems().map((row) => {
            const node = nodes[row.index];

            if (node === undefined) {
              return null;
            }

            return (
              <div
                key={node.deviceId}
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
                    href={`/devices/${node.deviceId}`}
                    onClick={(event) => {
                      // Left click with no modifier goes through the router; anything else is
                      // left to the browser, which is the point of this being a real anchor.
                      if (event.metaKey || event.ctrlKey || event.shiftKey || event.button !== 0) {
                        return;
                      }

                      event.preventDefault();
                      void navigate({
                        to: '/devices/$deviceId',
                        params: { deviceId: node.deviceId },
                      });
                    }}
                    className="rounded-control text-primary hover:text-accent focus-visible:ring-2 focus-visible:ring-accent focus-visible:outline-none"
                  >
                    {node.hostname}
                  </a>
                </span>
                <span
                  role="cell"
                  className={cn('flex w-24 shrink-0 items-center gap-2', stateTones[node.state])}
                >
                  <span className="h-1.5 w-1.5 rounded-full bg-current" aria-hidden="true" />
                  {stateLabels[node.state]}
                </span>
                <span role="cell" className="w-28 shrink-0 truncate">
                  {roleLabels[node.role]}
                </span>
                <span role="cell" className="w-32 shrink-0 truncate">
                  {node.site ?? '—'}
                </span>
                <span role="cell" className="w-20 shrink-0 tabular-nums">
                  {node.componentIndex + 1}
                </span>
                <span role="cell" className="w-20 shrink-0 tabular-nums">
                  {node.rank}
                </span>
                <span role="cell" className="w-20 shrink-0 tabular-nums">
                  {node.degree}
                </span>
                <span role="cell" className="w-28 shrink-0 tabular-nums">
                  {node.externalEdgeCount === 0 ? '—' : node.externalEdgeCount}
                </span>
              </div>
            );
          })}
        </div>
      </div>
    </div>
  );
}
