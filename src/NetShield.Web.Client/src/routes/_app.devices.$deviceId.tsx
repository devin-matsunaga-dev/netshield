import { createFileRoute } from '@tanstack/react-router';

import { DeviceDetailPage } from '@/features/devices/components/DeviceDetailPage';
import { deviceTabs, type DeviceTab } from '@/features/devices/components/deviceTabs';

/**
 * One device. The active tab is a search parameter, so a tab is a place that can be linked to,
 * refreshed and arrived at from the list's row menu.
 *
 * An unrecognised `tab` falls back to the overview rather than rendering nothing, for the same
 * reason the list drops a filter it does not know: a hand-edited address should still arrive
 * somewhere. Absent is also the overview, so the plain device URL carries no search parameter.
 */
export const Route = createFileRoute('/_app/devices/$deviceId')({
  validateSearch: (search: Record<string, unknown>): { tab?: DeviceTab } =>
    typeof search['tab'] === 'string' && (deviceTabs as readonly string[]).includes(search['tab'])
      ? { tab: search['tab'] as DeviceTab }
      : {},
  component: function DeviceDetail() {
    return (
      <DeviceDetailPage
        deviceId={Route.useParams().deviceId}
        tab={Route.useSearch().tab ?? 'overview'}
      />
    );
  },
});
