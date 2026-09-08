/**
 * The client screen's tabs, in the order a person reads them: what it is and where it is now,
 * then which addresses it has held, then which ports have reported it.
 *
 * A module of its own because the route validates the `tab` search parameter against this list
 * and the page renders from it — and a file that exports both a component and a constant makes
 * fast refresh give up on the component.
 */
export const clientTabs = ['overview', 'addresses', 'ports'] as const;

export type ClientTab = (typeof clientTabs)[number];
