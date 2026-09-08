import { useQuery } from '@tanstack/react-query';

import { Badge } from '@/components/ui/Badge';
import { Card } from '@/components/ui/Card';
import { Skeleton } from '@/components/ui/Skeleton';
import { Timestamp } from '@/components/ui/Timestamp';
import { formatRoundTrip, toNumber } from '@/lib/apiNumber';
import { deviceReachabilityQuery, type DeviceDetail } from '@/features/devices/api/deviceQueries';
import { DetailList, DetailRow } from '@/features/devices/components/DetailList';
import { DeviceStateBadge } from '@/features/devices/components/DeviceStateBadge';
import {
  criticalityLabels,
  environmentLabels,
  roleLabels,
  vendorLabels,
} from '@/features/devices/components/deviceLabels';

/**
 * What an operator maintains about a device, and the evidence behind the state NetShield
 * publishes for it.
 *
 * The reachability panel exists because a state on its own is not enough to act on: a device
 * nothing has been able to probe since Tuesday is `Online` with a `lastProbeAt` that quietly
 * stopped moving, and only `lastError` tells the two apart.
 */
export function DeviceOverviewTab({ device }: { readonly device: DeviceDetail }) {
  const reachability = useQuery(deviceReachabilityQuery(device.id));

  return (
    <div className="grid grid-cols-1 gap-gutter lg:grid-cols-2">
      <Card title="Inventory">
        <DetailList>
          <DetailRow label="Hostname">{device.hostname}</DetailRow>
          <DetailRow label="Primary IP address" mono>
            {device.primaryIpAddress}
          </DetailRow>
          <DetailRow label="Vendor">{vendorLabels[device.vendor]}</DetailRow>
          <DetailRow label="Model">{device.model}</DetailRow>
          <DetailRow label="OS version">{device.osVersion}</DetailRow>
          <DetailRow label="Serial number" mono>
            {device.serialNumber}
          </DetailRow>
          <DetailRow label="Role">{roleLabels[device.role]}</DetailRow>
          <DetailRow label="Site">{device.site}</DetailRow>
          <DetailRow label="Criticality">{criticalityLabels[device.criticality]}</DetailRow>
          <DetailRow label="Environment">{environmentLabels[device.environment]}</DetailRow>
          <DetailRow label="Owner">{device.owner}</DetailRow>
          <DetailRow label="Added">
            <Timestamp value={device.createdAt} />
          </DetailRow>
        </DetailList>

        {device.tags.length > 0 && (
          <div className="mt-gutter flex flex-wrap gap-2">
            {device.tags.map((tag) => (
              <Badge key={tag} tone="accent">
                {tag}
              </Badge>
            ))}
          </div>
        )}

        {device.notes !== null && device.notes !== '' && (
          <p className="mt-gutter text-body whitespace-pre-wrap text-secondary">{device.notes}</p>
        )}
      </Card>

      <Card title="Reachability">
        {reachability.isPending ? (
          <div
            role="status"
            className="space-y-3"
            aria-busy="true"
            aria-label="Loading reachability"
          >
            <Skeleton className="w-40" />
            <Skeleton className="w-56" />
            <Skeleton className="w-32" />
          </div>
        ) : reachability.data === null || reachability.data === undefined ? (
          <p className="text-body text-secondary">
            Nothing has probed this device yet. It joins the reachability schedule on the next pass,
            and its state stays unknown until then.
          </p>
        ) : (
          <>
            <DetailList>
              <DetailRow label="State">
                <DeviceStateBadge state={reachability.data.state} />
              </DetailRow>
              <DetailRow label="Last round trip">
                {toNumber(reachability.data.lastRttMilliseconds) === null
                  ? undefined
                  : formatRoundTrip(reachability.data.lastRttMilliseconds)}
              </DetailRow>
              <DetailRow label="Last packet loss">
                {toNumber(reachability.data.lastLossPercent) === null
                  ? undefined
                  : `${toNumber(reachability.data.lastLossPercent)?.toString() ?? ''}%`}
              </DetailRow>
              <DetailRow label="Last probed">
                <Timestamp value={reachability.data.lastProbeAt} />
              </DetailRow>
              <DetailRow label="State last changed">
                <Timestamp value={reachability.data.lastChangedAt} />
              </DetailRow>
              <DetailRow label="Next probe">
                <Timestamp value={reachability.data.nextProbeAt} />
              </DetailRow>
            </DetailList>

            {/*
              The whole reason this panel exists. A failed probe leaves the state alone by
              design, so without saying so the screen would show a confident state resting on
              evidence that stopped arriving.
            */}
            {reachability.data.lastError !== null && (
              <p role="status" className="mt-gutter text-metric-caption text-warning">
                The last probe could not be performed: {reachability.data.lastError} The state above
                is from the last probe that ran.
              </p>
            )}
          </>
        )}
      </Card>
    </div>
  );
}
