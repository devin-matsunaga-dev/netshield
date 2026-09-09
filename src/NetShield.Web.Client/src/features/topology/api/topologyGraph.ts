import type { Schemas } from '@/api/types';
import { requireNumber, toNumber } from '@/lib/apiNumber';

type GraphPage = Schemas['TopologyGraph'];

/** One device on the canvas. The contract's shape, with every number read as a number. */
export interface TopologyNode {
  readonly deviceId: string;
  readonly hostname: string;
  readonly vendor: Schemas['DeviceVendor'];
  readonly role: Schemas['DeviceRole'];
  readonly site: string | null;
  readonly state: Schemas['DeviceState'];
  readonly componentIndex: number;
  readonly rank: number;
  readonly degree: number;
  /** Live edges leading somewhere that is not a monitored device, so has no tile to draw. */
  readonly externalEdgeCount: number;
  readonly x: number;
  readonly y: number;
}

/** One link on the canvas, between two devices that are both on it. */
export interface TopologyEdge {
  readonly id: string;
  readonly aDeviceId: string;
  readonly aIfIndex: number;
  readonly aInterfaceName: string | null;
  readonly bDeviceId: string;
  readonly bIfIndex: number | null;
  readonly bInterfaceName: string | null;
  readonly sources: readonly Schemas['NeighborSource'][];
  readonly confidence: Schemas['AdjacencyConfidence'];
  readonly bidirectional: boolean;
  readonly componentIndex: number;
  readonly lastSeenAt: string;
}

/** One island of the estate. */
export interface TopologyComponent {
  readonly index: number;
  readonly rootDeviceId: string;
  readonly nodeCount: number;
  readonly edgeCount: number;
  readonly depth: number;
}

/** The geometry the coordinates were computed in, returned so the two cannot disagree. */
export interface TopologyLayout {
  readonly nodeWidth: number;
  readonly nodeHeight: number;
  readonly rankSeparation: number;
  readonly nodeSeparation: number;
  readonly componentSeparation: number;
}

/** How many nodes are in each device state. Every member is present, including the zeroes. */
export type StateCounts = Record<Schemas['DeviceState'], number>;

/** The whole accumulated graph, as the canvas and the table both read it. */
export interface TopologyGraphView {
  readonly nodes: readonly TopologyNode[];
  readonly edges: readonly TopologyEdge[];
  readonly components: readonly TopologyComponent[];
  readonly layout: TopologyLayout;
  /** How many nodes the filter matched, across every page — not how many arrived. */
  readonly totalNodeCount: number;
  readonly totalEdgeCount: number;
  /** Whether the API cut the graph because the estate is larger than one graph may hold. */
  readonly truncated: boolean;
  /**
   * Edges the API returned whose far end never arrived. Zero unless the graph was truncated:
   * an edge is returned with the earlier of its endpoints and both are nodes of the graph, so a
   * reader who accumulates every page holds both. They are counted rather than drawn, because
   * an edge to a node that is not there is a line to nowhere.
   */
  readonly danglingEdgeCount: number;
  readonly stateCounts: StateCounts;
}

/** Every device state, in the order the legend and the summary read them (DESIGN.md §3). */
export const deviceStates: readonly Schemas['DeviceState'][] = [
  'Online',
  'Warning',
  'Offline',
  'Unknown',
];

/** The empty graph, which is what an estate nothing has walked yet looks like. */
export const emptyGraph: TopologyGraphView = {
  nodes: [],
  edges: [],
  components: [],
  // DESIGN.md §6's node tile, and what the API sends when it has nothing else to say. Only ever
  // read before the first page arrives, at which point the server's own geometry replaces it.
  layout: {
    nodeWidth: 96,
    nodeHeight: 80,
    rankSeparation: 96,
    nodeSeparation: 48,
    componentSeparation: 128,
  },
  totalNodeCount: 0,
  totalEdgeCount: 0,
  truncated: false,
  danglingEdgeCount: 0,
  stateCounts: { Online: 0, Warning: 0, Offline: 0, Unknown: 0 },
};

/**
 * Accumulates the cursor pages of `GET /api/v1/topology/graph` into one graph.
 *
 * **Why a graph needs accumulating at all.** A page is 200 nodes, so a 500-device estate is
 * three requests, and a page of nodes is meaningless without the edges incident to it — the
 * canvas cannot draw anything until it has them all. WP-2.3 shaped the response for exactly
 * this: nodes are ordered component, then rank, then device id, and the pages concatenate in
 * that order, so the accumulated node list is already in graph order and nothing here re-sorts
 * it. That order is also the order the keyboard walks the canvas in.
 *
 * **Edges arrive once and are kept once.** Each is returned with the earlier of its two
 * endpoints, so the contract already guarantees no duplicate. They are merged through a map
 * keyed by adjacency id anyway, which costs nothing and makes this a function of the pages
 * rather than of the order they were fetched in — a refetch that overlaps cannot draw a link
 * twice.
 *
 * **An edge with an endpoint that never arrived is counted, not drawn.** `aDeviceIncluded` and
 * `bDeviceIncluded` describe one page; what matters here is the accumulated node set, which is
 * the only thing the canvas can draw against.
 *
 * A pure function over the pages, so the paging behaviour is provable as arithmetic rather than
 * only through a rendered canvas.
 */
export function accumulateGraph(pages: readonly GraphPage[]): TopologyGraphView {
  const last = pages.at(-1);

  if (last === undefined) {
    return emptyGraph;
  }

  const nodes = pages.flatMap((page) => page.nodes.map(readNode));
  const byDeviceId = new Set(nodes.map((node) => node.deviceId));

  const edges = new Map<string, TopologyEdge>();
  let danglingEdgeCount = 0;

  for (const page of pages) {
    for (const edge of page.edges) {
      if (!byDeviceId.has(edge.aDeviceId) || !byDeviceId.has(edge.bDeviceId)) {
        danglingEdgeCount++;
        continue;
      }

      edges.set(edge.id, readEdge(edge));
    }
  }

  const stateCounts: StateCounts = { Online: 0, Warning: 0, Offline: 0, Unknown: 0 };

  for (const node of nodes) {
    stateCounts[node.state]++;
  }

  return {
    nodes,
    edges: [...edges.values()],
    // Every page carries every component of the whole filtered graph, so the last page's list is
    // the complete one and is what a partially-loaded graph should describe itself by.
    components: last.components.map(readComponent),
    layout: readLayout(last.layout),
    totalNodeCount: requireNumber(last.totalNodeCount),
    totalEdgeCount: requireNumber(last.totalEdgeCount),
    truncated: pages.some((page) => page.truncated),
    danglingEdgeCount,
    stateCounts,
  };
}

function readNode(node: GraphPage['nodes'][number]): TopologyNode {
  return {
    deviceId: node.deviceId,
    hostname: node.hostname,
    vendor: node.vendor,
    role: node.role,
    site: node.site,
    state: node.state,
    componentIndex: requireNumber(node.componentIndex),
    rank: requireNumber(node.rank),
    degree: requireNumber(node.degree),
    externalEdgeCount: requireNumber(node.externalEdgeCount),
    x: requireNumber(node.x),
    y: requireNumber(node.y),
  };
}

function readEdge(edge: GraphPage['edges'][number]): TopologyEdge {
  return {
    id: edge.id,
    aDeviceId: edge.aDeviceId,
    aIfIndex: requireNumber(edge.aIfIndex),
    aInterfaceName: edge.aInterfaceName,
    bDeviceId: edge.bDeviceId,
    bIfIndex: toNumber(edge.bIfIndex),
    bInterfaceName: edge.bInterfaceName,
    sources: edge.sources,
    confidence: edge.confidence,
    bidirectional: edge.bidirectional,
    componentIndex: requireNumber(edge.componentIndex),
    lastSeenAt: edge.lastSeenAt,
  };
}

function readComponent(component: GraphPage['components'][number]): TopologyComponent {
  return {
    index: requireNumber(component.index),
    rootDeviceId: component.rootDeviceId,
    nodeCount: requireNumber(component.nodeCount),
    edgeCount: requireNumber(component.edgeCount),
    depth: requireNumber(component.depth),
  };
}

function readLayout(layout: GraphPage['layout']): TopologyLayout {
  return {
    nodeWidth: requireNumber(layout.nodeWidth),
    nodeHeight: requireNumber(layout.nodeHeight),
    rankSeparation: requireNumber(layout.rankSeparation),
    nodeSeparation: requireNumber(layout.nodeSeparation),
    componentSeparation: requireNumber(layout.componentSeparation),
  };
}
