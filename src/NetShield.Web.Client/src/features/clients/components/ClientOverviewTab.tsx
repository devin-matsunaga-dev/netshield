import { Link } from '@tanstack/react-router';

import { Badge } from '@/components/ui/Badge';
import { Card } from '@/components/ui/Card';
import { Timestamp } from '@/components/ui/Timestamp';
import type { ClientDetail } from '@/features/clients/api/clientQueries';
import {
  describePort,
  formatDuration,
  formatOui,
  observationSourceLabels,
} from '@/features/clients/components/clientLabels';
import { DetailList, DetailRow } from '@/features/devices/components/DetailList';
import { toNumber } from '@/lib/apiNumber';

/**
 * What this client is, and everything that is true about it right now.
 *
 * Every open binding rather than one of each. A client legitimately holds an IPv4 and an IPv6
 * address at once, and is legitimately reported by its access switch *and* by every switch above
 * it — because a MAC is learned by every bridge on the path to it. Showing one and hiding the
 * rest would be picking an answer NetShield does not have: which port is the edge is a topology
 * question, and topology is Phase 2's.
 *
 * `describePort` is what makes the list readable in the meantime. It is the evidence read out
 * loud rather than a guess — a port that learned one address is where something is plugged in,
 * and a port that learned two hundred is carrying everything behind it.
 */
export function ClientOverviewTab({ client }: { readonly client: ClientDetail }) {
  return (
    <div className="space-y-gutter">
      <Card title="Identity">
        <DetailList>
          <DetailRow label="MAC address" mono>
            {client.macAddress}
          </DetailRow>
          <DetailRow label="Vendor prefix" mono={!client.locallyAdministered}>
            {formatOui(client.oui, client.locallyAdministered)}
          </DetailRow>
          <DetailRow label="Hostname">{client.hostname}</DetailRow>
          <DetailRow label="Address type">
            {client.locallyAdministered ? 'Locally administered' : 'Universally administered'}
          </DetailRow>
          <DetailRow label="First seen">
            <Timestamp value={client.firstSeenAt} />
          </DetailRow>
          <DetailRow label="Last seen">
            <Timestamp value={client.lastSeenAt} />
          </DetailRow>
        </DetailList>

        {client.locallyAdministered && (
          <p className="mt-gutter text-metric-caption text-muted">
            The vendor prefix of a locally administered address stands for no manufacturer — nobody
            registered it. This is what address randomisation on a phone or a laptop looks like.
          </p>
        )}
      </Card>

      <Card title="Addresses held now">
        {client.ipBindings.length === 0 ? (
          <p className="text-body text-secondary">
            No address is currently bound to this client. It has been seen in a forwarding database
            but not in any ARP table, which is an ordinary state for something that has not spoken
            IP recently.
          </p>
        ) : (
          <ul className="space-y-3">
            {client.ipBindings.map((binding) => (
              <li key={binding.id} className="flex flex-wrap items-center gap-3">
                <Badge tone="violet">{binding.ipAddress}</Badge>
                <span className="text-body text-secondary">
                  held for {formatDuration(binding.observedFrom, binding.observedTo)}
                </span>
                <span className="text-metric-caption text-muted">
                  {observationSourceLabels[binding.source]}
                  {binding.deviceHostname === null ? '' : ` on ${binding.deviceHostname}`}
                </span>
              </li>
            ))}
          </ul>
        )}
      </Card>

      <Card title="Ports reporting it now">
        {client.portBindings.length === 0 ? (
          <p className="text-body text-secondary">
            No switch currently reports this client. It has been seen in an ARP table but not in any
            forwarding database — which is what a host behind a router, or beyond the switches
            NetShield reads, looks like.
          </p>
        ) : (
          <>
            <ul className="space-y-3">
              {client.portBindings.map((binding) => {
                const ifIndex = toNumber(binding.ifIndex);
                const vlanId = toNumber(binding.vlanId);
                const description = describePort(toNumber(binding.macCountOnPort));

                return (
                  <li key={binding.id} className="flex flex-wrap items-center gap-3">
                    {binding.deviceHostname === null ? (
                      <span className="text-body text-primary">A device that has been removed</span>
                    ) : (
                      <Link
                        to="/devices/$deviceId"
                        params={{ deviceId: binding.deviceId }}
                        className="rounded-control text-body text-accent focus-visible:ring-2 focus-visible:ring-accent focus-visible:outline-none"
                      >
                        {binding.deviceHostname}
                      </Link>
                    )}
                    <span className="font-mono text-body text-secondary">
                      {binding.interfaceName ?? `ifIndex ${ifIndex?.toString() ?? '—'}`}
                    </span>
                    {vlanId !== null && (
                      <span className="text-metric-caption text-muted">VLAN {vlanId}</span>
                    )}
                    {description !== null && (
                      <span className="text-metric-caption text-muted">{description}</span>
                    )}
                  </li>
                );
              })}
            </ul>

            {client.portBindings.length > 1 && (
              <p className="mt-gutter text-metric-caption text-muted">
                Several switches report this client because a MAC address is learned by every bridge
                on the path to it. The port that learned the fewest addresses is the one it is most
                likely plugged into; deciding properly needs the topology graph.
              </p>
            )}
          </>
        )}
      </Card>
    </div>
  );
}
