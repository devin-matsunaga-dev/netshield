import { deviceStates, type TopologyGraphView } from '@/features/topology/api/topologyGraph';
import { stateLabels } from '@/features/topology/components/topologyLabels';

interface TopologySummaryProps {
  readonly graph: TopologyGraphView;
  /** So the canvas can point at it with `aria-describedby`. */
  readonly id: string;
}

/**
 * The text summary DESIGN.md §9.7 requires of every chart, beside the table fallback.
 *
 * Not drawn: the reference screenshot's topology card carries no such line, and §9 admits no
 * invented visual direction. It is read instead — the canvas points at it with
 * `aria-describedby`, so a reader who cannot see five hundred tiles is told what they add up to
 * before deciding whether to walk them.
 *
 * It says the three things the picture cannot: how much of the estate is on the map, how it
 * breaks down by state, and where the map is knowingly incomplete — an island count, because
 * WP-2.3 returns an unreachable segment as a disconnected component rather than dropping it, and
 * the links that lead somewhere NetShield does not monitor and therefore cannot draw.
 */
export function TopologySummary({ graph, id }: TopologySummaryProps) {
  const external = graph.nodes.reduce((total, node) => total + node.externalEdgeCount, 0);

  const states = deviceStates
    .filter((state) => graph.stateCounts[state] > 0)
    .map((state) => `${graph.stateCounts[state].toString()} ${stateLabels[state].toLowerCase()}`)
    .join(', ');

  return (
    <p id={id} className="sr-only">
      {`${count(graph.nodes.length, 'device')} and ${count(graph.edges.length, 'link')}, in ` +
        `${count(graph.components.length, 'connected group')}.` +
        (states === '' ? '' : ` ${states}.`) +
        (external === 0
          ? ''
          : ` ${count(external, 'link')} ${external === 1 ? 'leads' : 'lead'} to something ` +
            `NetShield does not monitor and ${external === 1 ? 'is' : 'are'} not drawn.`) +
        (graph.truncated
          ? ' The estate is larger than one map can hold, so the map is cut short.'
          : '') +
        (graph.danglingEdgeCount === 0
          ? ''
          : ` ${count(graph.danglingEdgeCount, 'link')} could not be drawn because the far ` +
            `${graph.danglingEdgeCount === 1 ? 'device is' : 'devices are'} not on the map.`)}
    </p>
  );
}

function count(value: number, noun: string): string {
  return `${value.toString()} ${value === 1 ? noun : `${noun}s`}`;
}
