/**
 * The query-key factory for the topology feature (CONVENTIONS.md §6). Keys are never written
 * inline: a cache entry that only one call site can name is one no other call site can
 * invalidate.
 *
 * The graph has one key today because WP-2.4 draws the whole estate and builds no filter
 * controls. The factory takes the shape of one anyway — `graph()` under `graphs()` — so that the
 * package which adds site, VLAN and root filters adds an argument rather than a second key
 * family, and the two readings of the graph cannot end up in different cache entries.
 */
export const topologyKeys = {
  all: ['topology'] as const,
  graphs: () => [...topologyKeys.all, 'graph'] as const,
  graph: () => [...topologyKeys.graphs()] as const,
};
