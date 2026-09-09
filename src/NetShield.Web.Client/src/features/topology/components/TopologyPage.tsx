import { useInfiniteQuery } from '@tanstack/react-query';
import { useNavigate } from '@tanstack/react-router';
import { useCallback, useEffect, useMemo } from 'react';

import { PageHeader } from '@/components/layout/PageHeader';
import { Card } from '@/components/ui/Card';
import { EmptyState } from '@/components/ui/EmptyState';
import { ErrorState } from '@/components/ui/ErrorState';
import { Skeleton } from '@/components/ui/Skeleton';
import { Tabs } from '@/components/ui/Tabs';
import { accumulateGraph } from '@/features/topology/api/topologyGraph';
import { topologyGraphQuery } from '@/features/topology/api/topologyQueries';
import { TopologyCanvas } from '@/features/topology/components/TopologyCanvas';
import { TopologyDeviceTable } from '@/features/topology/components/TopologyDeviceTable';
import { TopologyLegend } from '@/features/topology/components/TopologyLegend';
import { TopologyLinkTable } from '@/features/topology/components/TopologyLinkTable';
import { TopologySummary } from '@/features/topology/components/TopologySummary';
import type { TopologyTab } from '@/features/topology/components/topologyTabs';

const tabs = [
  { id: 'map', label: 'Map' },
  { id: 'table', label: 'Table' },
] as const;

/** What the canvas and the tables are described by. Fixed, because there is one of each. */
const summaryId = 'topology-summary';

interface TopologyPageProps {
  readonly tab: TopologyTab;
}

/**
 * The Network screen: the estate as a graph.
 *
 * **It reads every page before it draws.** A page is 200 nodes and a graph is not a list — a
 * page of nodes is meaningless without the edges incident to it, so the screen asks for the next
 * cursor page as soon as the last one lands and keeps going until there is none. At the target
 * estate that is three requests. There is no "load more" control because there is nothing a
 * reader could usefully decide: half a map is not half as useful, it is wrong.
 *
 * **The map and the table are one query.** DESIGN.md §9.7 asks for a table fallback beside every
 * chart, and a fallback that fetched the graph a second time would be a second graph — the two
 * could disagree the moment a walk landed between the requests, and the reader would have no way
 * to tell which was the map they were looking at.
 */
export function TopologyPage({ tab }: TopologyPageProps) {
  const navigate = useNavigate();
  const query = useInfiniteQuery(topologyGraphQuery());

  const { hasNextPage, isFetchingNextPage, fetchNextPage } = query;

  /**
   * How many pages have landed. It is a dependency of the effect below and it is the only one
   * that reliably moves.
   *
   * On a graph of three pages or more, `hasNextPage` is `true` before *and* after the page that
   * has just arrived, and React can coalesce the fetching render away entirely — so an effect
   * keyed on those two booleans alone sees identical dependencies, does not re-run, and the
   * third page is never asked for. A two-page graph hides it, because `hasNextPage` goes false
   * and the dependencies change. That is 400 devices working and 401 silently drawing 400,
   * which is under `SPEC.md` §1's target estate. Found by rendering 500.
   */
  const pagesLoaded = query.data?.pages.length ?? 0;

  // In an effect rather than during render: asking for a page is a side effect, and React calls
  // a component's body more than once for reasons that have nothing to do with the graph.
  useEffect(() => {
    if (hasNextPage && !isFetchingNextPage) {
      void fetchNextPage();
    }
  }, [pagesLoaded, hasNextPage, isFetchingNextPage, fetchNextPage]);

  const graph = useMemo(() => accumulateGraph(query.data?.pages ?? []), [query.data]);

  const openDevice = useCallback(
    (deviceId: string) => {
      void navigate({ to: '/devices/$deviceId', params: { deviceId } });
    },
    [navigate],
  );

  const changeTab = useCallback(
    (next: string) => {
      void navigate({ to: '/network', search: { tab: next as TopologyTab }, replace: true });
    },
    [navigate],
  );

  // Still arriving: the first page has landed but the cursor has not run out. The map draws what
  // it has and says so, rather than holding a blank card until the last page.
  const partial = hasNextPage || isFetchingNextPage;

  return (
    <>
      <PageHeader
        title="Network"
        subtitle={subtitle(graph.totalNodeCount, graph.totalEdgeCount, query.isSuccess)}
      />

      <div className="mb-gutter">
        <Tabs label="Topology views" tabs={tabs} active={tab} onChange={changeTab} />
      </div>

      <div role="tabpanel" id={`panel-${tab}`} aria-labelledby={`tab-${tab}`}>
        {query.isPending ? (
          // A skeleton matching the final layout, not a centred spinner (CONVENTIONS.md §6).
          <Card title="Network topology">
            <div role="status" aria-busy="true" aria-label="Loading the topology map">
              <Skeleton className="h-[60vh] w-full" />
            </div>
          </Card>
        ) : query.isError ? (
          <Card title="Network topology">
            <ErrorState
              title="The topology map could not be loaded."
              action="NetShield could not reach the API. Check that it is running and try again."
              onRetry={() => void query.refetch()}
            />
          </Card>
        ) : graph.nodes.length === 0 ? (
          <Card title="Network topology">
            <EmptyState
              title="No topology yet."
              action="NetShield builds the map from LLDP, CDP and routing data. Wait for the topology schedule to reach a device, or check that a device has a credential profile that can walk it."
            />
          </Card>
        ) : tab === 'map' ? (
          <Card
            title="Network topology"
            control={<TopologyLegend stateCounts={graph.stateCounts} />}
          >
            <TopologySummary graph={graph} id={summaryId} />
            <TopologyCanvas graph={graph} onOpenDevice={openDevice} describedBy={summaryId} />
            <Notes
              partial={partial}
              truncated={graph.truncated}
              loaded={graph.nodes.length}
              total={graph.totalNodeCount}
            />
          </Card>
        ) : (
          <div className="flex flex-col gap-gutter">
            <TopologySummary graph={graph} id={summaryId} />
            <Card title="Devices on the map">
              <TopologyDeviceTable nodes={graph.nodes} />
              <Notes
                partial={partial}
                truncated={graph.truncated}
                loaded={graph.nodes.length}
                total={graph.totalNodeCount}
              />
            </Card>
            <Card title="Links on the map">
              <TopologyLinkTable nodes={graph.nodes} edges={graph.edges} />
            </Card>
          </div>
        )}
      </div>
    </>
  );
}

interface NotesProps {
  readonly partial: boolean;
  readonly truncated: boolean;
  readonly loaded: number;
  readonly total: number;
}

/**
 * What the map is not showing yet, or will never show.
 *
 * Two different things, deliberately separated. *Partial* is the cursor still running and will
 * resolve itself; *truncated* is the API saying the estate is larger than one graph may hold,
 * which will not. A reader who cannot find a switch deserves to know which of the two they are
 * looking at.
 */
function Notes({ partial, truncated, loaded, total }: NotesProps) {
  if (!partial && !truncated) {
    return null;
  }

  return (
    <p role="status" className="pt-3 text-metric-caption text-muted">
      {partial
        ? `Loading the rest of the map — ${loaded.toString()} of ${total.toString()} devices so far.`
        : `The estate is larger than one map can hold. This map shows ${loaded.toString()} of ${total.toString()} devices.`}
    </p>
  );
}

function subtitle(nodes: number, edges: number, loaded: boolean): string {
  if (!loaded) {
    return 'The L2 and L3 topology built from neighbour, ARP and routing data.';
  }

  const devices = nodes === 1 ? '1 device' : `${nodes.toString()} devices`;
  const links = edges === 1 ? '1 link' : `${edges.toString()} links`;

  return `${devices} and ${links} on the map, from neighbour, ARP and routing data.`;
}
