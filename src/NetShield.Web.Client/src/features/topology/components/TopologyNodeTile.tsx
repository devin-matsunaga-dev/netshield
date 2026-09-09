import { Handle, Position, type Node, type NodeProps } from '@xyflow/react';
import { useCallback } from 'react';

import type { TopologyNode } from '@/features/topology/api/topologyGraph';
import { useTopologyFocus } from '@/features/topology/components/topologyFocus';
import {
  describeExternalEdges,
  nodeSubLabel,
  roleIcons,
  roleLabels,
  stateTiles,
  stateLabels,
} from '@/features/topology/components/topologyLabels';
import { cn } from '@/lib/cn';

/** The one node type this canvas has. A node is a device (WP-2.3); nothing else gets a tile. */
export const deviceNodeType = 'device';

/** What React Flow carries for one tile. */
export type DeviceFlowNode = Node<{ readonly device: TopologyNode }, typeof deviceNodeType>;

/**
 * A topology node (DESIGN.md §6): a 48px icon tile with a 12px/500 label beneath and a 10px/400
 * `text-muted` sub-label, its border encoding state through §3's fixed mapping.
 *
 * A real `<button>` rather than a styled div. It is the thing a reader clicks to open the
 * device, so it has to be the thing a keyboard reaches and a screen reader announces — and its
 * accessible name carries what the tile itself cannot: the state in words, because DESIGN.md
 * §9.2 admits no colour as the only signal, and the links the map is not drawing.
 *
 * Four handles, all invisible. React Flow anchors an edge to a handle, and the canvas orients
 * each edge so it leaves the lower-ranked tile's bottom and arrives at the higher one's top —
 * or crosses left to right between two tiles of the same rank. Nothing here is connectable:
 * the graph is read from the estate, and a hand-drawn link would be a claim with no evidence
 * behind it.
 */
export function TopologyNodeTile({ data, width }: NodeProps<DeviceFlowNode>) {
  const device = data.device;
  const { activeDeviceId, register, focus, activate } = useTopologyFocus();

  const Icon = roleIcons[device.role];
  const isActive = activeDeviceId === device.deviceId;

  const ref = useCallback(
    (element: HTMLButtonElement | null) => {
      register(device.deviceId, element);
    },
    [register, device.deviceId],
  );

  const site = device.site === null ? '' : ` at ${device.site}`;
  const label =
    `${device.hostname}, ${roleLabels[device.role]}${site}. ` +
    `${stateLabels[device.state]}. ` +
    `${device.degree === 1 ? '1 link' : `${device.degree.toString()} links`} on the map.` +
    describeExternalEdges(device.externalEdgeCount);

  return (
    <>
      <Handle id="top" type="target" position={Position.Top} isConnectable={false} />
      <Handle id="left" type="target" position={Position.Left} isConnectable={false} />

      <button
        ref={ref}
        type="button"
        // Roving: one tile is in the tab order and the arrow keys move which one.
        tabIndex={isActive ? 0 : -1}
        aria-label={label}
        onFocus={() => {
          focus(device.deviceId);
        }}
        onClick={() => {
          activate(device.deviceId);
        }}
        // The width is the server's own `layout.nodeWidth`, passed to React Flow and read back
        // here, so the tile occupies exactly the cell the coordinates were computed for
        // (CONVENTIONS.md §6 admits an inline style for computed geometry).
        style={width === undefined ? undefined : { width: `${width.toString()}px` }}
        // `nopan` is React Flow's own opt-out, and it is the right answer twice over: pressing
        // a button should activate the button rather than begin dragging the map underneath it,
        // and the canvas still pans from everywhere else.
        className="nopan flex flex-col items-center gap-1 rounded-control focus-visible:ring-2 focus-visible:ring-accent focus-visible:outline-none"
      >
        <span
          className={cn(
            'flex h-node-tile w-node-tile items-center justify-center rounded-tile border-2 transition-colors duration-hover',
            stateTiles[device.state],
          )}
        >
          <Icon size={20} aria-hidden="true" />
        </span>
        <span className="w-full truncate text-node-label text-primary">{device.hostname}</span>
        <span className="w-full truncate text-node-sublabel text-muted">
          {nodeSubLabel(device.role, device.externalEdgeCount)}
        </span>
      </button>

      <Handle id="bottom" type="source" position={Position.Bottom} isConnectable={false} />
      <Handle id="right" type="source" position={Position.Right} isConnectable={false} />
    </>
  );
}
