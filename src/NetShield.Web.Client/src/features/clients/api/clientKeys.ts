import type { ClientListFilters } from '@/features/clients/api/clientFilters';

/**
 * The query-key factory for the clients feature (CONVENTIONS.md §6). Keys are never written
 * inline: a cache entry that only one call site can name is one no other call site can
 * invalidate.
 *
 * The list key carries the whole filter object, so two different filters are two different cache
 * entries. The resolution key carries the address and the instant, because a resolution *is* a
 * question about a pair — asking about the same address at a different moment is a different
 * question with a different answer.
 */
export const clientKeys = {
  all: ['clients'] as const,
  lists: () => [...clientKeys.all, 'list'] as const,
  list: (filters: ClientListFilters) => [...clientKeys.lists(), filters] as const,
  details: () => [...clientKeys.all, 'detail'] as const,
  detail: (id: string) => [...clientKeys.details(), id] as const,
  ipHistory: (id: string) => [...clientKeys.detail(id), 'ip-history'] as const,
  portHistory: (id: string) => [...clientKeys.detail(id), 'port-history'] as const,
  resolutions: () => [...clientKeys.all, 'resolve'] as const,
  resolution: (ipAddress: string, at: string | undefined) =>
    [...clientKeys.resolutions(), ipAddress, at ?? 'now'] as const,
};
