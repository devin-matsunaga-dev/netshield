import { describe, expect, it } from 'vitest';

import type { Schemas } from '@/api/types';
import { accumulateGraph, emptyGraph } from '@/features/topology/api/topologyGraph';
import { makeGraphEdge, makeGraphNode, testLayout } from '@/test/msw/topologyApi';

type GraphPage = Schemas['TopologyGraph'];

function page(overrides: Partial<GraphPage> = {}): GraphPage {
  return {
    nodes: [],
    edges: [],
    components: [],
    layout: testLayout,
    nextCursor: null,
    totalNodeCount: 0,
    totalEdgeCount: 0,
    truncated: false,
    ...overrides,
  };
}

const a = makeGraphNode({ deviceId: 'a', hostname: 'fw-01', rank: 0 });
const b = makeGraphNode({ deviceId: 'b', hostname: 'core-01', rank: 1, state: 'Warning' });
const c = makeGraphNode({ deviceId: 'c', hostname: 'acc-01', rank: 2, state: 'Offline' });

/**
 * The paging rules WP-2.3 settled, asserted as arithmetic. A graph is only usable once every
 * page has arrived, so the accumulation is the part that has to be right before anything is
 * drawn at all — and a rendered canvas is a poor place to find out that an edge went missing at
 * a page boundary.
 */
describe('accumulating the graph pages', () => {
  it('is the empty graph before any page has arrived', () => {
    expect(accumulateGraph([])).toBe(emptyGraph);
  });

  it('keeps the nodes in the order the pages delivered them', () => {
    const graph = accumulateGraph([
      page({ nodes: [a, b] }),
      page({ nodes: [c], totalNodeCount: 3 }),
    ]);

    expect(graph.nodes.map((node) => node.deviceId)).toEqual(['a', 'b', 'c']);
  });

  it('keeps an edge that straddles a page boundary', () => {
    // Returned with the page holding the earlier endpoint, which is the page `b` is not on.
    const graph = accumulateGraph([
      page({
        nodes: [a, b],
        edges: [
          makeGraphEdge({ id: 'e1', aDeviceId: 'b', bDeviceId: 'c', bDeviceIncluded: false }),
        ],
      }),
      page({ nodes: [c] }),
    ]);

    expect(graph.edges.map((edge) => edge.id)).toEqual(['e1']);
  });

  it('keeps an edge once even if two pages carry it', () => {
    const edge = makeGraphEdge({ id: 'e1', aDeviceId: 'a', bDeviceId: 'b' });

    const graph = accumulateGraph([
      page({ nodes: [a], edges: [edge] }),
      page({ nodes: [b], edges: [edge] }),
    ]);

    expect(graph.edges).toHaveLength(1);
  });

  it('counts an edge whose far device never arrived rather than drawing a line to nowhere', () => {
    const graph = accumulateGraph([
      page({
        nodes: [a, b],
        edges: [
          makeGraphEdge({ id: 'e1', aDeviceId: 'a', bDeviceId: 'b' }),
          makeGraphEdge({ id: 'e2', aDeviceId: 'b', bDeviceId: 'cut', bDeviceIncluded: false }),
        ],
        truncated: true,
      }),
    ]);

    expect(graph.edges.map((edge) => edge.id)).toEqual(['e1']);
    expect(graph.danglingEdgeCount).toBe(1);
  });

  it('breaks the nodes down by state, including the states nothing is in', () => {
    const graph = accumulateGraph([page({ nodes: [a, b, c] })]);

    expect(graph.stateCounts).toEqual({ Online: 1, Warning: 1, Offline: 1, Unknown: 0 });
  });

  it('reads the components and the geometry from the last page', () => {
    const components = [
      { index: 0, rootDeviceId: 'a', nodeCount: 2, edgeCount: 1, depth: 1 },
      { index: 1, rootDeviceId: 'c', nodeCount: 1, edgeCount: 0, depth: 0 },
    ];

    const graph = accumulateGraph([
      page({ nodes: [a, b], components }),
      page({ nodes: [c], components, totalNodeCount: 3, totalEdgeCount: 1 }),
    ]);

    expect(graph.components.map((component) => component.rootDeviceId)).toEqual(['a', 'c']);
    expect(graph.layout).toEqual(testLayout);
    expect(graph.totalNodeCount).toBe(3);
    expect(graph.totalEdgeCount).toBe(1);
  });

  it('is truncated if any page said so, not only the last', () => {
    const graph = accumulateGraph([page({ nodes: [a], truncated: true }), page({ nodes: [b] })]);

    expect(graph.truncated).toBe(true);
  });

  it('reads a number the API wrote as a string, because the contract says it might', () => {
    const graph = accumulateGraph([
      page({
        nodes: [makeGraphNode({ deviceId: 'a', rank: '3', degree: '2', x: '48', y: '176' })],
        totalNodeCount: '1',
      }),
    ]);

    expect(graph.nodes[0]).toMatchObject({ rank: 3, degree: 2, x: 48, y: 176 });
    expect(graph.totalNodeCount).toBe(1);
  });
});
