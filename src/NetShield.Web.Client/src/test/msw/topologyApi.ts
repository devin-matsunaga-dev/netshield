import { http, HttpResponse, type RequestHandler } from 'msw';

import type { Schemas } from '@/api/types';

type TopologyGraph = Schemas['TopologyGraph'];
type TopologyGraphNode = Schemas['TopologyGraphNode'];
type TopologyGraphEdge = Schemas['TopologyGraphEdge'];
type TopologyGraphComponent = Schemas['TopologyGraphComponent'];
type TopologyGraphLayout = Schemas['TopologyGraphLayout'];

const at = '2026-09-09T09:00:00.000Z';

/**
 * The topology graph a test sees.
 *
 * Held as one whole graph and served in cursor pages, rather than as pre-baked pages. The
 * screen's whole job is to accumulate every page before it draws, so a fixture that handed it
 * the answer in one response would test the drawing and never the accumulating — and the paging
 * is where the interesting rules are: an edge is returned with the page holding the *earlier* of
 * its two endpoints, and the two `included` flags describe that page rather than the graph.
 *
 * Every shape here is the generated one, so a fixture that drifts from the contract fails to
 * type-check rather than passing a test that lies about what the API sends. The numeric members
 * are written as plain numbers, which is what the API actually writes — the contract's
 * `number | string` is the document describing what it will *accept*.
 */
export interface TopologyApiState {
  nodes: TopologyGraphNode[];
  edges: TopologyGraphEdge[];
  components: TopologyGraphComponent[];
  layout: TopologyGraphLayout;
  /** The API's own answer to "is the estate larger than one graph may hold". */
  truncated: boolean;
  /** Turn the read into a 500, to reach the error state (DESIGN.md §8). */
  failGraph: boolean;
  /** Every graph request the SPA made, so a test can assert on the paging it actually did. */
  readonly reads: { cursor: string | null; limit: string | null }[];
}

/** DESIGN.md §6's node tile, which is the geometry WP-2.3 lays out in. */
export const testLayout: TopologyGraphLayout = {
  nodeWidth: 96,
  nodeHeight: 80,
  rankSeparation: 96,
  nodeSeparation: 48,
  componentSeparation: 128,
};

export function makeGraphNode(overrides: Partial<TopologyGraphNode> = {}): TopologyGraphNode {
  return {
    deviceId: '019226b4-1000-7000-8000-000000000001',
    hostname: 'core-sw-01',
    vendor: 'CiscoIos',
    role: 'Switch',
    site: 'HQ',
    state: 'Online',
    componentIndex: 0,
    rank: 0,
    degree: 1,
    externalEdgeCount: 0,
    x: 0,
    y: 0,
    ...overrides,
  };
}

export function makeGraphEdge(overrides: Partial<TopologyGraphEdge> = {}): TopologyGraphEdge {
  return {
    id: '019226b4-3000-7000-8000-000000000001',
    aDeviceId: '019226b4-1000-7000-8000-000000000001',
    aIfIndex: 1,
    aInterfaceName: 'GigabitEthernet1/0/1',
    bDeviceId: '019226b4-1000-7000-8000-000000000002',
    bIfIndex: 2,
    bInterfaceName: 'GigabitEthernet1/0/2',
    sources: ['Lldp'],
    confidence: 'Confirmed',
    bidirectional: true,
    componentIndex: 0,
    // Rewritten per page by the handler: they describe the page, not the graph.
    aDeviceIncluded: true,
    bDeviceIncluded: true,
    lastSeenAt: at,
    ...overrides,
  };
}

/**
 * The estate the reference screenshot's topology card draws: a firewall at the top, two core
 * switches under it with a link between them, and an access switch on each.
 *
 * Laid out the way `GraphLayoutRule` would — rank down the y axis at `nodeHeight` plus
 * `rankSeparation`, ranks centred against the widest — so that a test asserting on where a tile
 * ended up is asserting against the real geometry rather than against numbers chosen here.
 */
export function makeReferenceEstate(): Pick<
  TopologyApiState,
  'nodes' | 'edges' | 'components' | 'layout'
> {
  const id = (n: number) => `019226b4-1000-7000-8000-00000000000${n.toString()}`;
  const edgeId = (n: number) => `019226b4-3000-7000-8000-00000000000${n.toString()}`;

  // One rank is 80 + 96 apart; one column is 96 + 48.
  const rankY = (rank: number) => rank * 176;
  const columnX = (column: number) => column * 144;

  const nodes = [
    makeGraphNode({
      deviceId: id(1),
      hostname: 'fw-01',
      role: 'Firewall',
      vendor: 'FortinetFortiOs',
      rank: 0,
      degree: 2,
      externalEdgeCount: 1,
      x: columnX(0.5),
      y: rankY(0),
    }),
    makeGraphNode({
      deviceId: id(2),
      hostname: 'core-sw-01',
      rank: 1,
      degree: 3,
      x: columnX(0),
      y: rankY(1),
    }),
    makeGraphNode({
      deviceId: id(3),
      hostname: 'core-sw-02',
      rank: 1,
      degree: 3,
      state: 'Warning',
      x: columnX(1),
      y: rankY(1),
    }),
    makeGraphNode({
      deviceId: id(4),
      hostname: 'acc-sw-01',
      rank: 2,
      degree: 1,
      site: 'Floor 1',
      x: columnX(0),
      y: rankY(2),
    }),
    makeGraphNode({
      deviceId: id(5),
      hostname: 'acc-sw-02',
      rank: 2,
      degree: 1,
      state: 'Offline',
      site: 'Floor 2',
      x: columnX(1),
      y: rankY(2),
    }),
  ];

  const edges = [
    makeGraphEdge({ id: edgeId(1), aDeviceId: id(1), bDeviceId: id(2) }),
    makeGraphEdge({ id: edgeId(2), aDeviceId: id(1), bDeviceId: id(3) }),
    // Two core switches at one rank: the canvas draws this one left to right.
    makeGraphEdge({ id: edgeId(3), aDeviceId: id(2), bDeviceId: id(3) }),
    makeGraphEdge({ id: edgeId(4), aDeviceId: id(2), bDeviceId: id(4) }),
    // Seen by one end only, from CDP alone — which the map draws exactly like the others and
    // only the table fallback and the accessible name tell apart.
    makeGraphEdge({
      id: edgeId(5),
      aDeviceId: id(3),
      bDeviceId: id(5),
      sources: ['Cdp'],
      confidence: 'Possible',
      bidirectional: false,
      bInterfaceName: null,
      bIfIndex: null,
    }),
  ];

  return {
    nodes,
    edges,
    components: [{ index: 0, rootDeviceId: id(1), nodeCount: 5, edgeCount: 5, depth: 2 }],
    layout: testLayout,
  };
}

export function createTopologyApi(overrides: Partial<TopologyApiState> = {}): TopologyApiState {
  return {
    nodes: [],
    edges: [],
    components: [],
    layout: testLayout,
    truncated: false,
    failGraph: false,
    reads: [],
    ...overrides,
  };
}

export function topologyHandlers(current: () => TopologyApiState): RequestHandler[] {
  return [
    http.get('/api/v1/topology/graph', ({ request }) => {
      const state = current();

      if (state.failGraph) {
        return HttpResponse.json({ status: 500, title: 'Server error' }, { status: 500 });
      }

      const query = new URL(request.url).searchParams;
      const cursor = query.get('cursor');
      const limit = query.get('limit');

      state.reads.push({ cursor, limit });

      const size = limit === null ? 50 : Number(limit);
      const from = cursor === null ? 0 : Number(cursor);
      const to = Math.min(from + size, state.nodes.length);

      const page = state.nodes.slice(from, to);
      const onPage = new Set(page.map((node) => node.deviceId));

      // The rule WP-2.3 settled: an edge is returned with the page holding the *earlier* of its
      // two endpoints, and the flags say which of them this page actually carries. So each edge
      // arrives exactly once and never gets dropped for straddling a boundary.
      const position = new Map(state.nodes.map((node, index) => [node.deviceId, index]));

      const edges = state.edges
        .filter((edge) => {
          const first = Math.min(
            position.get(edge.aDeviceId) ?? Number.MAX_SAFE_INTEGER,
            position.get(edge.bDeviceId) ?? Number.MAX_SAFE_INTEGER,
          );

          return first >= from && first < to;
        })
        .map((edge) => ({
          ...edge,
          aDeviceIncluded: onPage.has(edge.aDeviceId),
          bDeviceIncluded: onPage.has(edge.bDeviceId),
        }));

      const body: TopologyGraph = {
        nodes: page,
        edges,
        // Every page carries every component of the whole filtered graph, not only those on it.
        components: state.components,
        layout: state.layout,
        nextCursor: to < state.nodes.length ? String(to) : null,
        totalNodeCount: state.nodes.length,
        totalEdgeCount: state.edges.length,
        truncated: state.truncated,
      };

      return HttpResponse.json(body);
    }),
  ];
}
