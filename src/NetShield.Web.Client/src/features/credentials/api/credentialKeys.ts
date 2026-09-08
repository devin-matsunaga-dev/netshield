import type { CredentialListFilters } from '@/features/credentials/api/credentialFilters';

/**
 * The query-key factory for the credentials feature (CONVENTIONS.md §6). Keys are never written
 * inline: a cache entry that only one call site can name is one no other call site can
 * invalidate.
 *
 * **No key here carries a secret, and none ever can.** A query key is held in memory for the life
 * of the cache and is visible in every devtools panel; the material a profile holds is write-only
 * over the API and is never read back, so there is nothing to key by even if somebody wanted to.
 * The rotation mutation deliberately has no key at all.
 */
export const credentialKeys = {
  all: ['credential-profiles'] as const,
  lists: () => [...credentialKeys.all, 'list'] as const,
  list: (filters: CredentialListFilters) => [...credentialKeys.lists(), filters] as const,
  details: () => [...credentialKeys.all, 'detail'] as const,
  detail: (id: string) => [...credentialKeys.details(), id] as const,
};
