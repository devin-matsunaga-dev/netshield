import { infiniteQueryOptions, queryOptions } from '@tanstack/react-query';

import { api } from '@/api/client';
import type { Schemas } from '@/api/types';
import { ApiError } from '@/features/session/api/currentUserQuery';
import type { DeviceListFilters } from '@/features/devices/api/deviceFilters';
import { deviceKeys } from '@/features/devices/api/deviceKeys';

export type DeviceSummary = Schemas['DeviceSummary'];
export type DeviceDetail = Schemas['DeviceDetail'];
export type DeviceFingerprintDetail = Schemas['DeviceFingerprintDetail'];
export type DeviceInterfaceSummary = Schemas['DeviceInterfaceSummary'];
export type DeviceReachabilityDetail = Schemas['DeviceReachabilityDetail'];
export type CredentialProfileSummary = Schemas['CredentialProfileSummary'];

/** How many rows a page carries. The API's own maximum is 200 (CONVENTIONS.md §4). */
const pageSize = 100;

/**
 * The device list, one cursor page at a time.
 *
 * An infinite query rather than a paged one: the table is virtualized and scrolls, so "load the
 * next page when the reader nears the end" is the interaction, and a page number nobody sees
 * would be a control invented for the sake of the transport.
 */
export function deviceListQuery(filters: DeviceListFilters) {
  return infiniteQueryOptions({
    queryKey: deviceKeys.list(filters),
    queryFn: async ({ pageParam, signal }) => {
      const { data, response } = await api.GET('/api/v1/devices', {
        params: {
          query: {
            limit: pageSize,
            ...(pageParam === undefined ? {} : { cursor: pageParam }),
            // Undefined members are removed rather than spread: `exactOptionalPropertyTypes`
            // tells "absent" from "present and undefined", and only the first of the two means
            // "do not filter on this".
            ...present(filters),
          },
        },
        signal,
      });

      if (!response.ok || data === undefined) {
        throw new ApiError('Could not load the device list.', response.status);
      }

      return data;
    },
    initialPageParam: undefined as string | undefined,
    // `null` is the API saying there is no next page; `undefined` is what TanStack Query reads
    // as "no more", so the two have to be reconciled here rather than at the call site.
    getNextPageParam: (last) => last.nextCursor ?? undefined,
  });
}

export function deviceQuery(id: string) {
  return queryOptions({
    queryKey: deviceKeys.detail(id),
    queryFn: async ({ signal }): Promise<DeviceDetail> => {
      const { data, response } = await api.GET('/api/v1/devices/{id}', {
        params: { path: { id } },
        signal,
      });

      if (!response.ok || data === undefined) {
        throw new ApiError('Could not load the device.', response.status);
      }

      return data;
    },
  });
}

/**
 * What the last walk established. A device nothing has walked answers 404, which is an ordinary
 * state rather than a failure — the tab says so and offers the walk.
 */
export function deviceFingerprintQuery(id: string) {
  return queryOptions({
    queryKey: deviceKeys.fingerprint(id),
    queryFn: async ({ signal }): Promise<DeviceFingerprintDetail | null> => {
      const { data, response } = await api.GET('/api/v1/devices/{id}/fingerprint', {
        params: { path: { id } },
        signal,
      });

      if (response.status === 404) {
        return null;
      }

      if (!response.ok || data === undefined) {
        throw new ApiError('Could not load the fingerprint.', response.status);
      }

      return data;
    },
    retry: false,
  });
}

/** The evidence behind the device's state. 404 means nothing has ever scheduled a probe. */
export function deviceReachabilityQuery(id: string) {
  return queryOptions({
    queryKey: deviceKeys.reachability(id),
    queryFn: async ({ signal }): Promise<DeviceReachabilityDetail | null> => {
      const { data, response } = await api.GET('/api/v1/devices/{id}/reachability', {
        params: { path: { id } },
        signal,
      });

      if (response.status === 404) {
        return null;
      }

      if (!response.ok || data === undefined) {
        throw new ApiError('Could not load the reachability detail.', response.status);
      }

      return data;
    },
    retry: false,
  });
}

/**
 * A device's interfaces. Paged, because a stacked switch answers with over a thousand and
 * CONVENTIONS.md §4 lets no endpoint return an unbounded collection.
 */
export function deviceInterfacesQuery(id: string) {
  return infiniteQueryOptions({
    queryKey: deviceKeys.interfaces(id),
    queryFn: async ({ pageParam, signal }) => {
      const { data, response } = await api.GET('/api/v1/devices/{id}/interfaces', {
        params: {
          path: { id },
          query: { limit: pageSize, ...(pageParam === undefined ? {} : { cursor: pageParam }) },
        },
        signal,
      });

      if (!response.ok || data === undefined) {
        throw new ApiError('Could not load the interface inventory.', response.status);
      }

      return data;
    },
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (last) => last.nextCursor ?? undefined,
  });
}

/**
 * A device's ports and what is on the other end of each.
 *
 * Not the interface inventory, although the two overlap: `/interfaces` is what a fingerprint
 * walk read about every interface the device has, and this is what is connected to the ones a
 * cable reaches. They share an `ifIndex` and nothing else.
 *
 * Paged for the same reason the interface list is — a stacked switch answers with over a
 * thousand rows — and a port carries its occupants inline, so a row is a whole answer rather
 * than a key into a second request per port.
 */
export function devicePortsQuery(id: string) {
  return infiniteQueryOptions({
    queryKey: deviceKeys.ports(id),
    queryFn: async ({ pageParam, signal }) => {
      const { data, response } = await api.GET('/api/v1/devices/{id}/ports', {
        params: {
          path: { id },
          query: { limit: pageSize, ...(pageParam === undefined ? {} : { cursor: pageParam }) },
        },
        signal,
      });

      if (!response.ok || data === undefined) {
        throw new ApiError('Could not load the port list.', response.status);
      }

      return data;
    },
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (last) => last.nextCursor ?? undefined,
  });
}

/**
 * The credential profiles assigned to a device, and every profile that could be.
 *
 * Behind `CredentialsManage`, which is Administrator-only (WP-1.2): a profile's username is half
 * of an SSH credential and the list of names says which accounts NetShield holds passwords for.
 * The tab is not drawn at all for a session that does not hold it, so this is never called by
 * one — the API refuses it either way.
 */
export function deviceCredentialProfilesQuery(id: string) {
  return queryOptions({
    queryKey: deviceKeys.credentialProfiles(id),
    queryFn: async ({ signal }): Promise<readonly CredentialProfileSummary[]> => {
      const { data, response } = await api.GET('/api/v1/devices/{deviceId}/credential-profiles', {
        params: { path: { deviceId: id } },
        signal,
      });

      if (!response.ok || data === undefined) {
        throw new ApiError('Could not load the assigned credential profiles.', response.status);
      }

      return data;
    },
  });
}

/** Every credential profile, for the assignment control to choose among. */
export function credentialProfileListQuery() {
  return queryOptions({
    queryKey: ['credential-profiles', 'list'] as const,
    queryFn: async ({ signal }): Promise<readonly CredentialProfileSummary[]> => {
      const { data, response } = await api.GET('/api/v1/credential-profiles', {
        params: { query: { limit: 200 } },
        signal,
      });

      if (!response.ok || data === undefined) {
        throw new ApiError('Could not load the credential profiles.', response.status);
      }

      return data.items;
    },
  });
}

/**
 * The members of an object that actually have a value.
 *
 * The return type drops `undefined` from each member rather than merely making it optional,
 * which is the distinction `exactOptionalPropertyTypes` is about and the whole reason this
 * exists.
 */
function present<T extends object>(source: T): { [K in keyof T]?: Exclude<T[K], undefined> } {
  return Object.fromEntries(Object.entries(source).filter(([, value]) => value !== undefined)) as {
    [K in keyof T]?: Exclude<T[K], undefined>;
  };
}

export type CollectorJobSummary = Schemas['CollectorJobSummary'];
export type CollectorJobStatus = Schemas['CollectorJobStatus'];

/**
 * A device's collector queue, newest first.
 *
 * Paged like every other list, and it matters here: a device polled every sixty seconds gains a
 * job a minute for as long as `collector_jobs` keeps them, and nothing prunes them yet.
 *
 * `refetchInterval` rather than a manual refresh, because the whole reason this screen exists is
 * to watch a job move — queued, leased, then finished. A screen you have to reload to see that
 * on is one that answers "did anything happen?" with "press F5 and find out".
 */
export function deviceJobsQuery(id: string, status: CollectorJobStatus | undefined) {
  return infiniteQueryOptions({
    queryKey: deviceKeys.jobList(id, status),
    queryFn: async ({ pageParam, signal }) => {
      const { data, response } = await api.GET('/api/v1/devices/{id}/jobs', {
        params: {
          path: { id },
          query: {
            limit: pageSize,
            ...(pageParam === undefined ? {} : { cursor: pageParam }),
            ...(status === undefined ? {} : { status }),
          },
        },
        signal,
      });

      if (!response.ok || data === undefined) {
        throw new ApiError('Could not load the job queue.', response.status);
      }

      return data;
    },
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (last) => last.nextCursor ?? undefined,
    refetchInterval: 5_000,
  });
}
