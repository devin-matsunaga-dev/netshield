/**
 * The device screen's tabs, in the order a person reads them: what it is, what a walk found,
 * what ports it has, how NetShield reaches it, and how to change any of that.
 *
 * A module of its own because the route validates the `tab` search parameter against this list
 * and the page renders from it — and a file that exports both a component and a constant makes
 * fast refresh give up on the component.
 */
export const deviceTabs = [
  'overview',
  'fingerprint',
  'interfaces',
  // Between what the device is and who may reach it: the queue is about what NetShield has
  // asked of the device, which is the question somebody has after pressing a walk button.
  'jobs',
  'credentials',
  'settings',
] as const;

export type DeviceTab = (typeof deviceTabs)[number];
