import { infiniteQueryOptions } from '@tanstack/react-query';

import { api } from '@/api/client';
import { ApiError } from '@/features/session/api/currentUserQuery';
import { topologyKeys } from '@/features/topology/api/topologyKeys';

/**
 * How many nodes a page carries. The API's own maximum (CONVENTIONS.md §4), and what WP-2.3
 * measured its "500 nodes in under 500 ms" against — three requests for the target estate.
 *
 * Larger than the 100 the device and client lists ask for, deliberately: those are virtualized
 * tables where a page is a screenful and the reader scrolls for the next, while a graph is
 * unusable until every page has arrived. Fewer round trips is the whole benefit here.
 */
export const graphPageSize = 200;

/**
 * The topology graph, one cursor page at a time.
 *
 * An infinite query, but not an infinite *scroll*: nothing about a graph is progressive, so the
 * page reads the pages one after another until `nextCursor` is null and draws when it has them
 * all. The pages are what the cache holds, and `accumulateGraph` folds them into the picture.
 *
 * There is no filter argument yet. WP-2.4 draws the whole estate; the endpoint's `site`,
 * `vlanId`, `rootDeviceId` and `depth` are a follow-on package (STATUS.md).
 */
export function topologyGraphQuery() {
  return infiniteQueryOptions({
    queryKey: topologyKeys.graph(),
    queryFn: async ({ pageParam, signal }) => {
      const { data, response } = await api.GET('/api/v1/topology/graph', {
        params: {
          query: {
            limit: graphPageSize,
            ...(pageParam === undefined ? {} : { cursor: pageParam }),
          },
        },
        signal,
      });

      if (!response.ok || data === undefined) {
        throw new ApiError('Could not load the topology graph.', response.status);
      }

      return data;
    },
    initialPageParam: undefined as string | undefined,
    // `null` is the API saying there is no next page; `undefined` is what TanStack Query reads
    // as "no more", so the two have to be reconciled here rather than at the call site.
    getNextPageParam: (last) => last.nextCursor ?? undefined,
  });
}
