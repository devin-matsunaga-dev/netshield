import type { DeviceListFilters } from '@/features/devices/api/deviceFilters';

/**
 * The query-key factory for the devices feature (CONVENTIONS.md §6). Keys are never written
 * inline: a cache entry that only one call site can name is one no other call site can
 * invalidate.
 *
 * The list key carries the whole filter object, so two different filters are two different cache
 * entries and changing one filter does not serve the previous one's rows. Every key hangs off
 * `all`, which is what a mutation invalidates when it does not know which lists are mounted.
 */
export const deviceKeys = {
  all: ['devices'] as const,
  lists: () => [...deviceKeys.all, 'list'] as const,
  list: (filters: DeviceListFilters) => [...deviceKeys.lists(), filters] as const,
  details: () => [...deviceKeys.all, 'detail'] as const,
  detail: (id: string) => [...deviceKeys.details(), id] as const,
  fingerprint: (id: string) => [...deviceKeys.detail(id), 'fingerprint'] as const,
  interfaces: (id: string) => [...deviceKeys.detail(id), 'interfaces'] as const,
  reachability: (id: string) => [...deviceKeys.detail(id), 'reachability'] as const,
  credentialProfiles: (id: string) => [...deviceKeys.detail(id), 'credential-profiles'] as const,
  // The status is part of the key because a filtered queue is a different question with a
  // different answer, and `jobs()` above it is what a walk or a cancellation invalidates without
  // having to know which filter the screen is currently on.
  jobs: (id: string) => [...deviceKeys.detail(id), 'jobs'] as const,
  jobList: (id: string, status: string | undefined) =>
    [...deviceKeys.jobs(id), status ?? 'all'] as const,
};
