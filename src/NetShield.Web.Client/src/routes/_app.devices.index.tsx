import { createFileRoute } from '@tanstack/react-router';

import { parseDeviceFilters, type DeviceListFilters } from '@/features/devices/api/deviceFilters';
import { DeviceListPage } from '@/features/devices/components/DeviceListPage';

/**
 * The device list. Its filters are the route's search parameters, which is what makes them
 * survive a refresh and travel in a link (WP-1.7's "filters compose and survive a refresh via
 * URL state").
 *
 * The filters are parsed twice, deliberately. `validateSearch` gives the route its type, and
 * then `parseDeviceFilters` runs again where the value is *used* — because a TanStack Router
 * route inherits its parent's search parameters and merges its own over them, so a value this
 * route rejected is still present as the root parsed it. WP-0.7 found the same trap in the
 * sign-in return path and settled it the same way: sanitise at the point of use. Without the
 * second pass, `?state=Melted` reaches the API and comes back a 400.
 */
export const Route = createFileRoute('/_app/devices/')({
  validateSearch: (search: Record<string, unknown>): DeviceListFilters =>
    parseDeviceFilters(search),
  component: function DeviceList() {
    return <DeviceListPage filters={parseDeviceFilters(Route.useSearch())} />;
  },
});
