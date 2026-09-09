import { useQuery } from '@tanstack/react-query';
import { Link, useNavigate } from '@tanstack/react-router';

import { PageHeader } from '@/components/layout/PageHeader';
import { Button } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';
import { ErrorState } from '@/components/ui/ErrorState';
import { Skeleton } from '@/components/ui/Skeleton';
import { Tabs, type TabDefinition } from '@/components/ui/Tabs';
import { deviceQuery } from '@/features/devices/api/deviceQueries';
import { DeviceCredentialsTab } from '@/features/devices/components/DeviceCredentialsTab';
import { DeviceFingerprintTab } from '@/features/devices/components/DeviceFingerprintTab';
import { DeviceInterfacesTab } from '@/features/devices/components/DeviceInterfacesTab';
import { DevicePortsTab } from '@/features/devices/components/DevicePortsTab';
import { DeviceJobsTab } from '@/features/devices/components/DeviceJobsTab';
import { DeviceOverviewTab } from '@/features/devices/components/DeviceOverviewTab';
import { DeviceSettingsTab } from '@/features/devices/components/DeviceSettingsTab';
import { DeviceStateBadge } from '@/features/devices/components/DeviceStateBadge';
import type { DeviceTab } from '@/features/devices/components/deviceTabs';
import { usePermissions } from '@/features/session/hooks/usePermissions';

interface DeviceDetailPageProps {
  readonly deviceId: string;
  readonly tab: DeviceTab;
}

/**
 * One device, in tabs.
 *
 * The active tab is a URL search parameter rather than component state, so a tab is a place that
 * can be linked to, refreshed and arrived at from the row menu — the same reason the list's
 * filters live in the address.
 *
 * Two tabs are hidden rather than disabled for a session that cannot use them. Credentials is
 * behind `CredentialsManage` because a profile's name and username are half a statement about
 * which accounts NetShield holds passwords for (WP-1.2); settings is behind `InventoryWrite`.
 * Both are refused by the API regardless — hiding spares a refusal rather than causing one.
 */
export function DeviceDetailPage({ deviceId, tab }: DeviceDetailPageProps) {
  const navigate = useNavigate();
  const holds = usePermissions();
  const device = useQuery(deviceQuery(deviceId));

  const tabs: TabDefinition[] = [
    { id: 'overview', label: 'Overview' },
    { id: 'fingerprint', label: 'Fingerprint' },
    { id: 'interfaces', label: 'Interfaces' },
    { id: 'ports', label: 'Ports' },
    { id: 'jobs', label: 'Jobs' },
    ...(holds('CredentialsManage') ? [{ id: 'credentials', label: 'Credentials' }] : []),
    ...(holds('InventoryWrite') ? [{ id: 'settings', label: 'Settings' }] : []),
  ];

  // A tab the session cannot see is not a tab it can be on — a bookmarked `?tab=settings` for a
  // read-only user lands on the overview rather than on a panel with no tab above it.
  const active = tabs.some((candidate) => candidate.id === tab) ? tab : 'overview';

  if (device.isPending) {
    return (
      <>
        <PageHeader title="Device" subtitle="Loading…" />
        <Card>
          <div role="status" className="space-y-3" aria-busy="true" aria-label="Loading the device">
            <Skeleton className="w-64" />
            <Skeleton className="w-48" />
            <Skeleton className="w-56" />
          </div>
        </Card>
      </>
    );
  }

  if (device.isError) {
    return (
      <>
        <PageHeader title="Device" subtitle="This device could not be loaded." />
        <Card>
          <ErrorState
            title="The device could not be loaded."
            action="It may have been removed, or the API may not be reachable. Try again, or go back to the device list."
            onRetry={() => void device.refetch()}
          />
        </Card>
      </>
    );
  }

  const detail = device.data;

  return (
    <>
      <div className="mb-gutter flex items-start justify-between gap-4">
        <div>
          <div className="flex items-center gap-3">
            <h1 className="text-page-title text-primary">{detail.hostname}</h1>
            <DeviceStateBadge state={detail.state} />
          </div>
          <p className="font-mono text-page-subtitle text-secondary">{detail.primaryIpAddress}</p>
        </div>
        <Link to="/devices">
          <Button variant="secondary">Back to devices</Button>
        </Link>
      </div>

      <div className="mb-gutter">
        <Tabs
          label="Device sections"
          tabs={tabs}
          active={active}
          onChange={(id) =>
            void navigate({
              to: '/devices/$deviceId',
              params: { deviceId },
              search: { tab: id as DeviceTab },
              replace: true,
            })
          }
        />
      </div>

      <div role="tabpanel" id={`panel-${active}`} aria-labelledby={`tab-${active}`}>
        {active === 'overview' && <DeviceOverviewTab device={detail} />}
        {active === 'fingerprint' && <DeviceFingerprintTab device={detail} />}
        {active === 'interfaces' && <DeviceInterfacesTab deviceId={deviceId} />}
        {active === 'ports' && <DevicePortsTab deviceId={deviceId} />}
        {active === 'jobs' && <DeviceJobsTab deviceId={deviceId} />}
        {active === 'credentials' && <DeviceCredentialsTab deviceId={deviceId} />}
        {active === 'settings' && <DeviceSettingsTab device={detail} />}
      </div>
    </>
  );
}
