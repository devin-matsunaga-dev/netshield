import { infiniteQueryOptions, queryOptions } from '@tanstack/react-query';

import { api } from '@/api/client';
import type { Schemas } from '@/api/types';
import { ApiError } from '@/features/session/api/currentUserQuery';
import { discoveryKeys } from '@/features/discovery/api/discoveryKeys';

export type DiscoveryCandidateSummary = Schemas['DiscoveryCandidateSummary'];
export type DiscoveryRunSummary = Schemas['DiscoveryRunSummary'];
export type DiscoveryRunDetail = Schemas['DiscoveryRunDetail'];
export type DiscoveryRunHostResult = Schemas['DiscoveryRunHostResult'];
export type DiscoverySeedSummary = Schemas['DiscoverySeedSummary'];
export type DiscoverySeedDetail = Schemas['DiscoverySeedDetail'];
export type DiscoveryIgnoreEntry = Schemas['DiscoveryIgnoreEntry'];

const pageSize = 100;

/**
 * The review list. Defaults to `New` at the call site, because the question the screen exists to
 * ask is "what has NetShield found that nobody has decided about".
 */
export function discoveryCandidatesQuery(status: Schemas['DiscoveryCandidateStatus'] | undefined) {
  return infiniteQueryOptions({
    queryKey: discoveryKeys.candidates(status),
    queryFn: async ({ pageParam, signal }) => {
      const { data, response } = await api.GET('/api/v1/discovery/candidates', {
        params: {
          query: {
            limit: pageSize,
            ...(pageParam === undefined ? {} : { cursor: pageParam }),
            ...(status === undefined ? {} : { status }),
          },
        },
        signal,
      });

      if (!response.ok || data === undefined) {
        throw new ApiError('Could not load the discovery candidates.', response.status);
      }

      return data;
    },
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (last) => last.nextCursor ?? undefined,
  });
}

export function discoveryRunsQuery(seedId: string | undefined) {
  return infiniteQueryOptions({
    queryKey: discoveryKeys.runs(seedId),
    queryFn: async ({ pageParam, signal }) => {
      const { data, response } = await api.GET('/api/v1/discovery/runs', {
        params: {
          query: {
            limit: pageSize,
            ...(pageParam === undefined ? {} : { cursor: pageParam }),
            ...(seedId === undefined ? {} : { seedId }),
          },
        },
        signal,
      });

      if (!response.ok || data === undefined) {
        throw new ApiError('Could not load the discovery runs.', response.status);
      }

      return data;
    },
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (last) => last.nextCursor ?? undefined,
  });
}

export function discoveryRunQuery(id: string) {
  return queryOptions({
    queryKey: discoveryKeys.run(id),
    queryFn: async ({ signal }): Promise<DiscoveryRunDetail> => {
      const { data, response } = await api.GET('/api/v1/discovery/runs/{id}', {
        params: { path: { id } },
        signal,
      });

      if (!response.ok || data === undefined) {
        throw new ApiError('Could not load the discovery run.', response.status);
      }

      return data;
    },
    // A run in flight is still being written to as its sweep jobs report back.
    refetchInterval: (query) =>
      query.state.data?.status === 'Pending' || query.state.data?.status === 'Running'
        ? 5_000
        : false,
  });
}

/**
 * A run's per-host outcomes.
 *
 * Only the addresses that answered are rows. A /16 that found nothing would otherwise write
 * sixty-five thousand rows to say so — the run itself records which ranges were swept and how
 * many addresses that was, which is where "was this address in scope" is answered.
 */
export function discoveryRunHostsQuery(
  id: string,
  outcome: Schemas['DiscoveryHostOutcome'] | undefined,
) {
  return infiniteQueryOptions({
    queryKey: discoveryKeys.runHosts(id, outcome),
    queryFn: async ({ pageParam, signal }) => {
      const { data, response } = await api.GET('/api/v1/discovery/runs/{id}/hosts', {
        params: {
          path: { id },
          query: {
            limit: pageSize,
            ...(pageParam === undefined ? {} : { cursor: pageParam }),
            ...(outcome === undefined ? {} : { outcome }),
          },
        },
        signal,
      });

      if (!response.ok || data === undefined) {
        throw new ApiError('Could not load the hosts this run found.', response.status);
      }

      return data;
    },
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (last) => last.nextCursor ?? undefined,
  });
}

export function discoverySeedsQuery() {
  return queryOptions({
    queryKey: discoveryKeys.seeds(),
    queryFn: async ({ signal }): Promise<readonly DiscoverySeedSummary[]> => {
      const { data, response } = await api.GET('/api/v1/discovery/seeds', {
        params: { query: { limit: 200 } },
        signal,
      });

      if (!response.ok || data === undefined) {
        throw new ApiError('Could not load the discovery seeds.', response.status);
      }

      return data.items;
    },
  });
}

export function discoverySeedQuery(id: string) {
  return queryOptions({
    queryKey: [...discoveryKeys.seeds(), id] as const,
    queryFn: async ({ signal }): Promise<DiscoverySeedDetail> => {
      const { data, response } = await api.GET('/api/v1/discovery/seeds/{id}', {
        params: { path: { id } },
        signal,
      });

      if (!response.ok || data === undefined) {
        throw new ApiError('Could not load the discovery seed.', response.status);
      }

      return data;
    },
  });
}

export function discoveryIgnoresQuery() {
  return queryOptions({
    queryKey: discoveryKeys.ignores(),
    queryFn: async ({ signal }): Promise<readonly DiscoveryIgnoreEntry[]> => {
      const { data, response } = await api.GET('/api/v1/discovery/ignores', {
        params: { query: { limit: 200 } },
        signal,
      });

      if (!response.ok || data === undefined) {
        throw new ApiError('Could not load the discovery ignore list.', response.status);
      }

      return data.items;
    },
  });
}
