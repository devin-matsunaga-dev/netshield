import {
  Background,
  BackgroundVariant,
  ReactFlow,
  ReactFlowProvider,
  useReactFlow,
  type Edge,
} from '@xyflow/react';
import { useCallback, useMemo, useRef, useState, type KeyboardEvent } from 'react';

import type { TopologyGraphView, TopologyNode } from '@/features/topology/api/topologyGraph';
import { TopologyControls } from '@/features/topology/components/TopologyControls';
import {
  deviceNodeType,
  TopologyNodeTile,
  type DeviceFlowNode,
} from '@/features/topology/components/TopologyNodeTile';
import {
  TopologyFocusContext,
  type TopologyFocusValue,
} from '@/features/topology/components/topologyFocus';
import { describeEdge, formatPort } from '@/features/topology/components/topologyLabels';
import { useMediaQuery } from '@/lib/useMediaQuery';

/** Module scope, not a render: React Flow re-mounts every node when this object's identity moves. */
const nodeTypes = { [deviceNodeType]: TopologyNodeTile };

/** The dot grid DESIGN.md §6 puts behind the canvas. */
const dotGridGap = 20;

/** DESIGN.md §7: 200ms on a panel, and nothing at all for a reader who asked for less motion. */
const panDuration = 200;

interface TopologyCanvasProps {
  readonly graph: TopologyGraphView;
  /** Opening the device behind a tile. */
  readonly onOpenDevice: (deviceId: string) => void;
  /** The text summary this canvas is described by (DESIGN.md §9.7). */
  readonly describedBy: string;
}

/**
 * The topology canvas (DESIGN.md §6): the dot grid, the node tiles, the edges between them, and
 * the zoom and fit controls stacked at the top-left.
 *
 * **The coordinates are the server's.** WP-2.3 computes the layered layout and returns the
 * geometry it used, so the canvas draws rather than lays out. A second `dagre` pass here would
 * be a second answer to a question already answered — and on a graph accumulated over three
 * pages the two could differ, which is a picture nobody could reproduce from a screenshot.
 *
 * **Nothing is virtualized and that is the performance decision, not an omission of one.**
 * React Flow pans and zooms by transforming one container, so neither costs a React render
 * however many tiles are on it; `onlyRenderVisibleElements` would replace that with a re-render
 * of the visible set on every frame of a pan, which is the opposite of what the 60fps criterion
 * wants at 500 nodes. What is done instead is to keep React out of the interaction entirely:
 * the node objects are memoized, `nodeTypes` is module scope, dragging and connecting are off,
 * and the keyboard's active tile travels by context so an arrow press never rebuilds the store.
 */
export function TopologyCanvas({ graph, onOpenDevice, describedBy }: TopologyCanvasProps) {
  return (
    // React Flow only creates its store for its own subtree, and the keyboard model here needs
    // to move the viewport from outside `<ReactFlow>`, so the provider is explicit.
    <ReactFlowProvider>
      <Canvas graph={graph} onOpenDevice={onOpenDevice} describedBy={describedBy} />
    </ReactFlowProvider>
  );
}

function Canvas({ graph, onOpenDevice, describedBy }: TopologyCanvasProps) {
  const { setCenter, getZoom } = useReactFlow();
  const stillness = useMediaQuery('(prefers-reduced-motion: reduce)');

  const tiles = useRef(new Map<string, HTMLButtonElement>());
  const [activeDeviceId, setActiveDeviceId] = useState<string | null>(null);

  // One tile is always in the tab order, including before anything has focused the canvas —
  // otherwise the whole map would be skipped by the first Tab that reached it.
  const active = activeDeviceId ?? graph.nodes[0]?.deviceId ?? null;

  const nodes = useMemo<DeviceFlowNode[]>(
    () =>
      graph.nodes.map((device) => ({
        id: device.deviceId,
        type: deviceNodeType,
        position: { x: device.x, y: device.y },
        // The server's own cell, so React Flow needs no measuring pass and the tile occupies
        // exactly the space the coordinates were computed for.
        width: graph.layout.nodeWidth,
        height: graph.layout.nodeHeight,
        data: { device },
        draggable: false,
        selectable: false,
        connectable: false,
        focusable: false,
        // React Flow switches a node's pointer events off when nothing about it is interactive
        // to React Flow — which is true here, because the interactive thing is the tile's own
        // button rather than anything React Flow owns. Turning them back on through the node's
        // `style` is the mechanism React Flow documents for exactly this, and it has to be on
        // the node rather than in a class: it is undoing an inline style.
        style: { pointerEvents: 'all' as const },
      })),
    [graph.nodes, graph.layout.nodeWidth, graph.layout.nodeHeight],
  );

  const edges = useMemo<Edge[]>(() => toFlowEdges(graph), [graph]);

  const register = useCallback((deviceId: string, element: HTMLButtonElement | null) => {
    if (element === null) {
      tiles.current.delete(deviceId);
    } else {
      tiles.current.set(deviceId, element);
    }
  }, []);

  const focus = useCallback((deviceId: string) => {
    setActiveDeviceId(deviceId);
  }, []);

  const activate = useCallback(
    (deviceId: string) => {
      onOpenDevice(deviceId);
    },
    [onOpenDevice],
  );

  const focusValue = useMemo<TopologyFocusValue>(
    () => ({ activeDeviceId: active, register, focus, activate }),
    [active, register, focus, activate],
  );

  /**
   * Moves the keyboard to a tile: real DOM focus, and the viewport brought to it.
   *
   * Focusing works synchronously because every tile is in the DOM — the reason nothing here is
   * virtualized. Centring rather than scrolling-into-view because the canvas is a transformed
   * pane rather than a scroll container, so "into view" is a viewport transform.
   */
  const moveTo = useCallback(
    (device: TopologyNode) => {
      setActiveDeviceId(device.deviceId);
      tiles.current.get(device.deviceId)?.focus();

      void setCenter(
        device.x + graph.layout.nodeWidth / 2,
        device.y + graph.layout.nodeHeight / 2,
        { zoom: getZoom(), duration: stillness ? 0 : panDuration },
      );
    },
    [setCenter, getZoom, graph.layout.nodeWidth, graph.layout.nodeHeight, stillness],
  );

  /**
   * The arrow keys walk the nodes in graph order — component, then rank within it, then device
   * id, which is the order WP-2.3 returns them in and the order the pages concatenate in. So a
   * reader crossing the map with a keyboard crosses the estate's main body from its root
   * outwards and reaches the islands last, which is the same journey the eye makes.
   */
  const onKeyDown = useCallback(
    (event: KeyboardEvent<HTMLDivElement>) => {
      if (graph.nodes.length === 0) {
        return;
      }

      const from = Math.max(
        graph.nodes.findIndex((node) => node.deviceId === active),
        0,
      );

      const to = nextIndex(event.key, from, graph.nodes.length);

      if (to === null) {
        return;
      }

      const device = graph.nodes[to];

      if (device === undefined) {
        return;
      }

      // Only once a key is known to be one of ours: an unclaimed key must still reach the page.
      event.preventDefault();
      moveTo(device);
    },
    [graph.nodes, active, moveTo],
  );

  return (
    <div
      role="group"
      aria-label="Topology map"
      aria-describedby={describedBy}
      onKeyDown={onKeyDown}
      className="h-[60vh] w-full overflow-hidden rounded-control"
    >
      <TopologyFocusContext value={focusValue}>
        <ReactFlow
          nodes={nodes}
          edges={edges}
          nodeTypes={nodeTypes}
          // Read-only, and every one of these says so. The graph comes from the estate; there is
          // no write route behind this canvas and there is not meant to be (WP-2.3).
          nodesDraggable={false}
          nodesConnectable={false}
          nodesFocusable={false}
          edgesFocusable={false}
          elementsSelectable={false}
          elevateNodesOnSelect={false}
          // React Flow's own arrow-key handling moves a selected node, which is a write. The
          // keyboard model here is the roving focus above.
          disableKeyboardA11y
          // The wheel belongs to the page: a canvas inside a scrolling screen that swallowed it
          // would trap a reader trying to get past the card. Zoom is the buttons and the pinch.
          zoomOnScroll={false}
          panOnScroll={false}
          zoomOnDoubleClick={false}
          fitView
          fitViewOptions={{ padding: 0.15, maxZoom: 1 }}
          proOptions={{ hideAttribution: true }}
        >
          <Background variant={BackgroundVariant.Dots} gap={dotGridGap} size={1} />
          <TopologyControls />
        </ReactFlow>
      </TopologyFocusContext>
    </div>
  );
}

/** Which node the key asks for, or `null` when the key is not one this canvas claims. */
function nextIndex(key: string, from: number, length: number): number | null {
  switch (key) {
    case 'ArrowRight':
    case 'ArrowDown':
      return Math.min(from + 1, length - 1);
    case 'ArrowLeft':
    case 'ArrowUp':
      return Math.max(from - 1, 0);
    case 'Home':
      return 0;
    case 'End':
      return length - 1;
    default:
      return null;
  }
}

/**
 * The edges, oriented so the drawing reads down the ranks the way the reference screenshot's
 * card does: out of the lower-ranked tile's bottom and into the higher one's top, or left to
 * right between two tiles of one rank.
 *
 * The orientation is presentation and nothing more. An adjacency has an `A` end and a `B` end
 * because WP-2.1 canonicalises them by device id so two switches' accounts of one cable land on
 * one row — which says nothing about which end is nearer the core, and would draw half the map
 * upside down if it were taken to.
 */
function toFlowEdges(graph: TopologyGraphView): Edge[] {
  const byDeviceId = new Map(graph.nodes.map((node) => [node.deviceId, node]));

  return graph.edges.flatMap((edge) => {
    const a = byDeviceId.get(edge.aDeviceId);
    const b = byDeviceId.get(edge.bDeviceId);

    // `accumulateGraph` has already dropped an edge whose far end never arrived; this is the
    // type narrowing rather than a second rule.
    if (a === undefined || b === undefined) {
      return [];
    }

    const aPort = formatPort(edge.aInterfaceName, edge.aIfIndex);
    const bPort = formatPort(edge.bInterfaceName, edge.bIfIndex);

    const [upper, lower, upperPort, lowerPort] = above(a, b)
      ? ([a, b, aPort, bPort] as const)
      : ([b, a, bPort, aPort] as const);

    const sideways = a.rank === b.rank;

    return [
      {
        id: edge.id,
        source: upper.deviceId,
        target: lower.deviceId,
        sourceHandle: sideways ? 'right' : 'bottom',
        targetHandle: sideways ? 'left' : 'top',
        // Right-angled, which is how the reference screenshot's topology card draws a link.
        type: 'smoothstep',
        focusable: false,
        selectable: false,
        // Everything the drawn line deliberately does not say. DESIGN.md §6 gives every edge one
        // appearance and §9 admits no invented visual direction, so confidence, agreement and
        // the protocols behind the claim are said here and in the table fallback instead.
        ariaLabel: describeEdge(
          upper.hostname,
          upperPort,
          lower.hostname,
          lowerPort,
          edge.confidence,
          edge.bidirectional,
          edge.sources,
        ),
      },
    ];
  });
}

/** Whether the first node is the one an edge should leave, by rank and then by where it sits. */
function above(a: TopologyNode, b: TopologyNode): boolean {
  if (a.rank !== b.rank) {
    return a.rank < b.rank;
  }

  if (a.x !== b.x) {
    return a.x < b.x;
  }

  // Ordered on something, so two identical requests draw one picture.
  return a.deviceId < b.deviceId;
}
