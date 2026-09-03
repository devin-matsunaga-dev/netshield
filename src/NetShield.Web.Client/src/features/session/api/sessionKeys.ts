/**
 * The query-key factory for the session feature (CONVENTIONS.md §6). Keys are never written
 * inline: a cache entry that only one call site can name is one no other call site can
 * invalidate.
 */
export const sessionKeys = {
  all: ['session'] as const,
  current: () => [...sessionKeys.all, 'current'] as const,
};
