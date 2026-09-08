/**
 * The discovery screen's tabs. Candidates is first and is the default, because it is the
 * question the screen exists to answer: what has NetShield found that nobody has decided about.
 *
 * A module of its own for the reason `deviceTabs` is one — the route validates against this list
 * and the page renders from it.
 */
export const discoveryTabs = ['candidates', 'runs', 'seeds', 'ignored'] as const;

export type DiscoveryTab = (typeof discoveryTabs)[number];
