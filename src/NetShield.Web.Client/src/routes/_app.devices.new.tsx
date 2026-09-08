import { createFileRoute } from '@tanstack/react-router';

import { AddDevicePage } from '@/features/devices/components/AddDevicePage';

/**
 * Adding a device.
 *
 * A route of its own rather than a modal over the list: it is a form long enough to want the
 * page, and an address a person can be sent to. Nothing guards it beyond the session — the
 * sidebar and the list hide the way in for a reader without `InventoryWrite`, and the API
 * refuses the POST regardless (ARCHITECTURE.md §8).
 */
export const Route = createFileRoute('/_app/devices/new')({
  component: AddDevicePage,
});
