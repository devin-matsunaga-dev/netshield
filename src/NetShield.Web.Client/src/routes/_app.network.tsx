import { createFileRoute } from '@tanstack/react-router';

import { TopologyPage } from '@/features/topology/components/TopologyPage';
import {
  parseTopologyTab,
  topologyTabs,
  type TopologyTab,
} from '@/features/topology/components/topologyTabs';

/**
 * The Network screen, which is the topology map and its table fallback.
 *
 * The active view is a search parameter, so the table is a place that can be linked to,
 * refreshed and arrived at — which matters more here than on a device's tabs: the table is the
 * accessible fallback for the canvas (DESIGN.md §9.7), and a fallback nobody can send someone a
 * link to is one they have to be told how to find.
 *
 * An unrecognised `tab` falls back to the map rather than rendering nothing, for the same reason
 * the device list drops a filter it does not know: a hand-edited address should still arrive
 * somewhere.
 */
export const Route = createFileRoute('/_app/network')({
  validateSearch: (search: Record<string, unknown>): { tab?: TopologyTab } =>
    typeof search['tab'] === 'string' && (topologyTabs as readonly string[]).includes(search['tab'])
      ? { tab: search['tab'] as TopologyTab }
      : {},
  component: function Network() {
    // Parsed again rather than trusted: a value `validateSearch` rejected still arrives here
    // as the root route parsed it. See `parseTopologyTab`.
    return <TopologyPage tab={parseTopologyTab(Route.useSearch().tab)} />;
  },
});
