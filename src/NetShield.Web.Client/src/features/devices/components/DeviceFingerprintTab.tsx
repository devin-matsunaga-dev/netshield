import { useQuery } from '@tanstack/react-query';

import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';
import { EmptyState } from '@/components/ui/EmptyState';
import { ErrorState } from '@/components/ui/ErrorState';
import { Skeleton } from '@/components/ui/Skeleton';
import { Timestamp } from '@/components/ui/Timestamp';
import { deviceFingerprintQuery, type DeviceDetail } from '@/features/devices/api/deviceQueries';
import { useQueueDeviceWalk } from '@/features/devices/api/deviceMutations';
import { DetailList, DetailRow } from '@/features/devices/components/DetailList';
import { formatUptime, vendorLabels } from '@/features/devices/components/deviceLabels';
import { toNumber } from '@/lib/apiNumber';
import { RequirePermission } from '@/features/session/components/RequirePermission';
import { useToast } from '@/lib/toast';

/** What each overridable field is called on screen, keyed by the name the API sends. */
const overriddenLabels: Record<string, string> = {
  vendor: 'Vendor',
  model: 'Model',
  osVersion: 'OS version',
  serialNumber: 'Serial number',
};

/**
 * What the last SNMP walk established, and which of those facts an operator has overruled.
 *
 * Two things on this tab are requirements rather than decoration. `reducedCapability` is
 * SPEC.md §4's "clearly-labelled reduced feature set" — the fact was recorded by WP-1.5 and had
 * no way onto a screen until now. And `overriddenFields` is the difference between what the
 * device says it is and what somebody typed; showing the walk's value beside the device's is the
 * only way that disagreement is visible rather than merely resolved.
 */
export function DeviceFingerprintTab({ device }: { readonly device: DeviceDetail }) {
  const fingerprint = useQuery(deviceFingerprintQuery(device.id));
  const walk = useQueueDeviceWalk(device.id);
  const toast = useToast();

  const walkButton = (
    <RequirePermission permission="DiscoveryRun">
      <Button
        variant="secondary"
        disabled={walk.isPending}
        onClick={() => {
          walk.mutate(undefined, {
            onSuccess: () => {
              toast.announce('Walk queued. The fingerprint updates when a collector reports back.');
            },
            onError: (error) => {
              toast.announce(error.message, 'danger');
            },
          });
        }}
      >
        Walk now
      </Button>
    </RequirePermission>
  );

  if (fingerprint.isPending) {
    return (
      <Card title="Fingerprint">
        <div
          role="status"
          className="space-y-3"
          aria-busy="true"
          aria-label="Loading the fingerprint"
        >
          <Skeleton className="w-48" />
          <Skeleton className="w-64" />
          <Skeleton className="w-40" />
        </div>
      </Card>
    );
  }

  if (fingerprint.isError) {
    return (
      <Card title="Fingerprint">
        <ErrorState
          title="The fingerprint could not be loaded."
          action="NetShield could not reach the API. Check that it is running and try again."
          onRetry={() => void fingerprint.refetch()}
        />
      </Card>
    );
  }

  if (fingerprint.data === null) {
    return (
      <Card title="Fingerprint">
        <EmptyState
          title="This device has not been fingerprinted."
          action="Walk it over SNMP to find out what it is. It needs an SNMP credential profile assigned first."
        >
          {walkButton}
        </EmptyState>
      </Card>
    );
  }

  const data = fingerprint.data;

  return (
    <div className="space-y-gutter">
      {/*
        SPEC.md §4: a device that fell back to generic SNMP has a reduced feature set and the UI
        has to say so clearly. It is drawn from the observation the walk recorded rather than
        inferred from the vendor name, so a device an operator pinned to a vendor does not
        thereby claim CLI features nothing has demonstrated it has.
      */}
      {data.reducedCapability && (
        <div
          role="status"
          className="flex items-start gap-3 rounded-card border border-warning bg-warning-tint p-gutter"
        >
          <Badge tone="warning">Reduced capability</Badge>
          <p className="text-body text-secondary">
            NetShield recognised this device over SNMP but has no CLI support for it. Config backup,
            drift detection and compliance assessment are unavailable; polling, reachability and the
            interface inventory work as normal.
          </p>
        </div>
      )}

      <Card title="What the walk read">
        <div className="mb-gutter flex items-center justify-between gap-4">
          <p className="text-metric-caption text-muted">
            Last walked <Timestamp value={data.lastWalkAt} />.
          </p>
          {walkButton}
        </div>

        <DetailList>
          <DetailRow label="Vendor">{vendorLabels[data.vendor]}</DetailRow>
          <DetailRow label="Model">{data.model}</DetailRow>
          <DetailRow label="OS version">{data.osVersion}</DetailRow>
          <DetailRow label="Serial number" mono>
            {data.serialNumber}
          </DetailRow>
          <DetailRow label="sysObjectID" mono>
            {data.sysObjectId}
          </DetailRow>
          <DetailRow label="sysName">{data.sysName}</DetailRow>
          <DetailRow label="sysContact">{data.sysContact}</DetailRow>
          <DetailRow label="sysLocation">{data.sysLocation}</DetailRow>
          <DetailRow label="Uptime">
            {/* What the agent said. sysUpTime wraps after about 497 days and nothing here
                reconstructs a boot time from one observation. */}
            {formatUptime(toNumber(data.uptimeSeconds))}
          </DetailRow>
          <DetailRow label="Interfaces">
            {data.interfaceCount.toString()}
            {data.interfacesTruncated ? ' (walk was truncated)' : ''}
          </DetailRow>
        </DetailList>

        {data.sysDescr !== null && (
          <div className="mt-gutter">
            <p className="text-metric-caption text-muted">sysDescr</p>
            <p className="font-mono text-table-cell break-words text-secondary">{data.sysDescr}</p>
          </div>
        )}

        {/*
          The walk owns these four facts unless the device's value differs from what the previous
          walk discovered — which is how "operator override" is answered without a provenance
          column. It is right in the ordinary cases and cannot see an operator who set a field
          back to the discovered value; recorded in STATUS.md rather than papered over here.
        */}
        {data.overriddenFields.length > 0 && (
          <div className="mt-gutter rounded-card border border-subtle p-gutter">
            <p className="text-metric-label text-secondary">Overridden by an operator</p>
            <p className="mt-1 text-metric-caption text-muted">
              The inventory disagrees with the last walk on{' '}
              {data.overriddenFields
                .map((field) => overriddenLabels[field] ?? field)
                .join(', ')
                .toLowerCase()}
              . A walk leaves an overridden field alone; the values above are what the device
              reported.
            </p>
          </div>
        )}

        {/*
          A failed walk records why and erases nothing. Without this, a device whose walks have
          been failing for a week is indistinguishable from one walked a week ago.
        */}
        {data.lastError !== null && (
          <p role="status" className="mt-gutter text-metric-caption text-warning">
            The last walk could not be performed: {data.lastError} Everything above is from the last
            walk that ran.
          </p>
        )}
      </Card>
    </div>
  );
}
