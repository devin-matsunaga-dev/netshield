import { useVirtualizer } from '@tanstack/react-virtual';
import { useMemo, useRef } from 'react';

import { Timestamp } from '@/components/ui/Timestamp';
import type { TopologyEdge, TopologyNode } from '@/features/topology/api/topologyGraph';
import {
  confidenceLabels,
  formatPort,
  formatSources,
} from '@/features/topology/components/topologyLabels';

/** DESIGN.md §6: rows are 44px. */
const rowHeight = 44;

interface TopologyLinkTableProps {
  readonly nodes: readonly TopologyNode[];
  readonly edges: readonly TopologyEdge[];
}

/**
 * The other half of the map's table fallback (DESIGN.md §9.7): every link on it, and what
 * supports the claim it is there.
 *
 * This is where the canvas's deliberate silences are said out loud. Every edge is drawn
 * identically — DESIGN.md §6 gives an edge one appearance and §9 admits no invented visual
 * direction — so a link seen by one end only, and a link a routing table inferred rather than
 * LLDP saw, look exactly like a link both switches agree on. The three columns that tell them
 * apart are here, and in the accessible name of the drawn edge, and nowhere else.
 *
 * Ordered as the API returned it, which is by the node ordering the edges arrived with, so a
 * reader can hold the two tables side by side.
 */
export function TopologyLinkTable({ nodes, edges }: TopologyLinkTableProps) {
  const scroller = useRef<HTMLDivElement>(null);

  const hostnames = useMemo(
    () => new Map(nodes.map((node) => [node.deviceId, node.hostname])),
    [nodes],
  );

  const virtualizer = useVirtualizer({
    count: edges.length,
    getScrollElement: () => scroller.current,
    estimateSize: () => rowHeight,
    overscan: 12,
  });

  return (
    <div
      role="table"
      aria-label="Links on the map"
      aria-rowcount={edges.length}
      className="flex flex-col"
    >
      <div
        role="row"
        className="flex items-center gap-4 border-b border-subtle px-gutter py-2 text-table-header text-muted"
      >
        <span role="columnheader" className="w-64 shrink-0">
          From
        </span>
        <span role="columnheader" className="w-64 shrink-0">
          To
        </span>
        <span role="columnheader" className="w-28 shrink-0">
          Confidence
        </span>
        <span role="columnheader" className="w-28 shrink-0">
          Reported by
        </span>
        <span role="columnheader" className="w-32 shrink-0">
          Seen with
        </span>
        <span role="columnheader" className="w-28 shrink-0">
          Last seen
        </span>
      </div>

      <div ref={scroller} className="max-h-[60vh] overflow-auto" data-testid="topology-links">
        <div
          className="relative w-full"
          style={{ height: `${virtualizer.getTotalSize().toString()}px` }}
        >
          {virtualizer.getVirtualItems().map((row) => {
            const edge = edges[row.index];

            if (edge === undefined) {
              return null;
            }

            return (
              <div
                key={edge.id}
                role="row"
                aria-rowindex={row.index + 1}
                className="absolute top-0 left-0 flex w-full items-center gap-4 border-b border-subtle px-gutter text-table-cell text-secondary transition-colors duration-hover hover:bg-raised"
                style={{
                  height: `${rowHeight.toString()}px`,
                  transform: `translateY(${row.start.toString()}px)`,
                }}
              >
                <span role="cell" className="w-64 shrink-0 truncate">
                  <span className="text-primary">
                    {hostnames.get(edge.aDeviceId) ?? edge.aDeviceId}
                  </span>{' '}
                  <span className="font-mono">
                    {formatPort(edge.aInterfaceName, edge.aIfIndex)}
                  </span>
                </span>
                <span role="cell" className="w-64 shrink-0 truncate">
                  <span className="text-primary">
                    {hostnames.get(edge.bDeviceId) ?? edge.bDeviceId}
                  </span>{' '}
                  <span className="font-mono">
                    {formatPort(edge.bInterfaceName, edge.bIfIndex)}
                  </span>
                </span>
                <span role="cell" className="w-28 shrink-0">
                  {confidenceLabels[edge.confidence]}
                </span>
                <span role="cell" className="w-28 shrink-0">
                  {edge.bidirectional ? 'Both ends' : 'One end'}
                </span>
                <span role="cell" className="w-32 shrink-0 truncate">
                  {formatSources(edge.sources)}
                </span>
                <span role="cell" className="w-28 shrink-0 tabular-nums">
                  <Timestamp value={edge.lastSeenAt} />
                </span>
              </div>
            );
          })}
        </div>
      </div>
    </div>
  );
}
