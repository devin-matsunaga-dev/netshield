/**
 * The Network screen's two views of one graph: the map, and the table fallback DESIGN.md §9.7
 * requires of every chart.
 *
 * A module of its own because the route validates the `tab` search parameter against this list
 * and the page renders from it — and a file that exports both a component and a constant makes
 * fast refresh give up on the component.
 */
export const topologyTabs = ['map', 'table'] as const;

export type TopologyTab = (typeof topologyTabs)[number];

/**
 * Reads the view out of the URL, falling back to the map for anything unrecognised.
 *
 * This runs twice, deliberately: once in the route's `validateSearch` and once here, where the
 * value is used. A TanStack Router route inherits its parent's search parameters and merges its
 * own over them, so a value this route rejected survives as the root parsed it — `?tab=elsewhere`
 * reached the page as `'elsewhere'` and rendered the table, because the table was the branch
 * anything-but-`'map'` fell into.
 *
 * WP-0.7 found this trap on the sign-in return path, WP-1.7 on the device filters, WP-1.8 on the
 * client filters, and it is here a fourth time. It is well past the point at which a shared
 * "validated search" helper is worth writing rather than each route remembering — recorded in
 * STATUS.md.
 */
export function parseTopologyTab(value: unknown): TopologyTab {
  return typeof value === 'string' && (topologyTabs as readonly string[]).includes(value)
    ? (value as TopologyTab)
    : 'map';
}
