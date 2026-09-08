import { infiniteQueryOptions, queryOptions } from '@tanstack/react-query';

import { api } from '@/api/client';
import type { Schemas } from '@/api/types';
import { seenSince, type ClientListFilters } from '@/features/clients/api/clientFilters';
import { clientKeys } from '@/features/clients/api/clientKeys';
import { ApiError } from '@/features/session/api/currentUserQuery';

export type ClientSummary = Schemas['ClientSummary'];
export type ClientDetail = Schemas['ClientDetail'];
export type ClientIpBindingSummary = Schemas['ClientIpBindingSummary'];
export type ClientPortBindingSummary = Schemas['ClientPortBindingSummary'];
export type AssetResolution = Schemas['AssetResolution'];

/** How many rows a page carries. The API's own maximum is 200 (CONVENTIONS.md §4). */
const pageSize = 100;

/**
 * The client list, one cursor page at a time.
 *
 * An infinite query rather than a paged one, for the reason the device list is: the table is
 * virtualized and scrolls, so "load the next page when the reader nears the end" is the
 * interaction and a page number nobody sees would be a control invented for the transport.
 */
export function clientListQuery(filters: ClientListFilters) {
  return infiniteQueryOptions({
    queryKey: clientKeys.list(filters),
    queryFn: async ({ pageParam, signal }) => {
      const { data, response } = await api.GET('/api/v1/clients', {
        params: {
          query: {
            limit: pageSize,
            ...(pageParam === undefined ? {} : { cursor: pageParam }),
            ...(filters.search === undefined ? {} : { search: filters.search }),
            ...(filters.deviceId === undefined ? {} : { deviceId: filters.deviceId }),
            ...(filters.vlanId === undefined ? {} : { vlanId: filters.vlanId }),
            // Resolved to an instant here rather than held in the URL: "since 11:04" means
            // something different tomorrow, and a pasted link should keep meaning what it said.
            ...(filters.seen === undefined ? {} : { seenSince: seenSince(filters.seen) }),
            ...(filters.onlyActive === true ? { onlyActive: true } : {}),
          },
        },
        signal,
      });

      if (!response.ok || data === undefined) {
        throw new ApiError('Could not load the client list.', response.status);
      }

      return data;
    },
    initialPageParam: undefined as string | undefined,
    // `null` is the API saying there is no next page; `undefined` is what TanStack Query reads
    // as "no more", so the two have to be reconciled here rather than at the call site.
    getNextPageParam: (last) => last.nextCursor ?? undefined,
  });
}

export function clientQuery(id: string) {
  return queryOptions({
    queryKey: clientKeys.detail(id),
    queryFn: async ({ signal }): Promise<ClientDetail> => {
      const { data, response } = await api.GET('/api/v1/clients/{id}', {
        params: { path: { id } },
        signal,
      });

      if (!response.ok || data === undefined) {
        throw new ApiError('Could not load the client.', response.status);
      }

      return data;
    },
  });
}

/**
 * A client's address history, newest interval first.
 *
 * Paged, and it matters: a device on a short lease in a guest VLAN gains an interval every time
 * it reconnects, and a year of that is thousands of rows for one client.
 */
export function clientIpHistoryQuery(id: string) {
  return infiniteQueryOptions({
    queryKey: clientKeys.ipHistory(id),
    queryFn: async ({ pageParam, signal }) => {
      const { data, response } = await api.GET('/api/v1/clients/{id}/ip-history', {
        params: {
          path: { id },
          query: { limit: pageSize, ...(pageParam === undefined ? {} : { cursor: pageParam }) },
        },
        signal,
      });

      if (!response.ok || data === undefined) {
        throw new ApiError('Could not load the address history.', response.status);
      }

      return data;
    },
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (last) => last.nextCursor ?? undefined,
  });
}

/** A client's port history, newest interval first. */
export function clientPortHistoryQuery(id: string) {
  return infiniteQueryOptions({
    queryKey: clientKeys.portHistory(id),
    queryFn: async ({ pageParam, signal }) => {
      const { data, response } = await api.GET('/api/v1/clients/{id}/port-history', {
        params: {
          path: { id },
          query: { limit: pageSize, ...(pageParam === undefined ? {} : { cursor: pageParam }) },
        },
        signal,
      });

      if (!response.ok || data === undefined) {
        throw new ApiError('Could not load the port history.', response.status);
      }

      return data;
    },
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (last) => last.nextCursor ?? undefined,
  });
}

/**
 * `ResolveAssetAt` over HTTP: what held an address at an instant.
 *
 * A 400 is an ordinary answer here rather than a failure — it is what the reader gets for typing
 * something that is not an address — so it resolves to `null` and the screen says so, the same
 * shape the fingerprint tab uses for a device nothing has walked.
 */
export function assetResolutionQuery(ipAddress: string, at: string | undefined) {
  return queryOptions({
    queryKey: clientKeys.resolution(ipAddress, at),
    queryFn: async ({ signal }): Promise<AssetResolution | null> => {
      const { data, response } = await api.GET('/api/v1/clients/resolve', {
        params: { query: { ipAddress, ...(at === undefined ? {} : { at }) } },
        signal,
      });

      if (response.status === 400) {
        return null;
      }

      if (!response.ok || data === undefined) {
        throw new ApiError('Could not resolve the address.', response.status);
      }

      return data;
    },
    retry: false,
  });
}
