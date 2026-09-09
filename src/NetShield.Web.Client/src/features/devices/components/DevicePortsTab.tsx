import { useInfiniteQuery } from '@tanstack/react-query';
import { useVirtualizer } from '@tanstack/react-virtual';
import { Link } from '@tanstack/react-router';
import { useEffect, useRef, useState } from 'react';

import { Badge } from '@/components/ui/Badge';
import { Card } from '@/components/ui/Card';
import { EmptyState } from '@/components/ui/EmptyState';
import { ErrorState } from '@/components/ui/ErrorState';
import { Skeleton } from '@/components/ui/Skeleton';
import { Timestamp } from '@/components/ui/Timestamp';
import type { Schemas } from '@/api/types';
import { devicePortsQuery } from '@/features/devices/api/deviceQueries';
import {
  capabilityLabels,
  confidenceLabels,
  neighborSourceLabels,
  portOccupancySummary,
  portRoleExplanation,
  portRoleLabels,
  portRoleTones,
} from '@/features/devices/components/portLabels';
import { interfaceStatusLabels } from '@/features/devices/components/deviceLabels';
import { requireNumber, toNumber } from '@/lib/apiNumber';

type WirePort = Schemas['DevicePortSummary'];

/**
 * A port with its numbers actually numbers.
 *
 * Every numeric member of the contract is generated as `number | string` — see `apiNumber.ts`,
 * and the STATUS.md note about the schema transformer that will one day make this unnecessary.
 * Coercing once here rather than at each of the eight places a count is read is what lets the
 * label functions below take plain numbers and be tested with them.
 */
interface Port extends Omit<
  WirePort,
  'ifIndex' | 'learnedAddressCount' | 'clientCount' | 'clients'
> {
  readonly ifIndex: number;
  readonly learnedAddressCount: number | null;
  readonly clientCount: number;
  readonly clients: readonly PortClient[];
}

interface PortClient extends Omit<WirePort['clients'][number], 'vlanId'> {
  readonly vlanId: number | null;
}

function readPort(row: WirePort): Port {
  return {
    ...row,
    ifIndex: requireNumber(row.ifIndex),
    learnedAddressCount: toNumber(row.learnedAddressCount),
    clientCount: requireNumber(row.clientCount),
    clients: row.clients.map((client) => ({ ...client, vlanId: toNumber(client.vlanId) })),
  };
}

const rowHeight = 44;

/**
 * A device's ports and what is on the other end of each.
 *
 * Beside the Interfaces tab rather than inside it: that one answers what a fingerprint walk read
 * about every interface the device has, loopbacks and switch virtual interfaces included, and
 * this answers what is connected to the ones a cable reaches. They share an `ifIndex` and
 * nothing else.
 *
 * **A row says what it concluded and why.** The access-versus-uplink reading is evidence rather
 * than something the network states, so the role badge never appears without the sentence that
 * produced it — and an uplink says how many addresses it carries instead of listing them, because
 * a MAC is learned by every bridge on the path to it and listing them would claim two hundred
 * hosts are plugged into one cable.
 */
export function DevicePortsTab({ deviceId }: { readonly deviceId: string }) {
  const ports = useInfiniteQuery(devicePortsQuery(deviceId));
  const scroller = useRef<HTMLDivElement>(null);
  const [expanded, setExpanded] = useState<number | null>(null);

  const rows: Port[] = ports.data?.pages.flatMap((page) => page.items.map(readPort)) ?? [];

  const virtualizer = useVirtualizer({
    count: rows.length,
    getScrollElement: () => scroller.current,
    estimateSize: () => rowHeight,
    overscan: 12,
  });

  const items = virtualizer.getVirtualItems();
  const lastVisible = items.at(-1)?.index ?? 0;

  useEffect(() => {
    if (
      rows.length > 0 &&
      lastVisible >= rows.length - 10 &&
      ports.hasNextPage &&
      !ports.isFetchingNextPage
    ) {
      void ports.fetchNextPage();
    }
  }, [rows.length, lastVisible, ports]);

  if (ports.isPending) {
    return (
      <Card title="Ports">
        <div role="status" className="space-y-3" aria-busy="true" aria-label="Loading ports">
          {Array.from({ length: 6 }, (_, index) => (
            <Skeleton key={index} className="h-8 w-full" />
          ))}
        </div>
      </Card>
    );
  }

  if (ports.isError) {
    return (
      <Card title="Ports">
        <ErrorState
          title="The port list could not be loaded."
          action="NetShield could not reach the API. Check that it is running and try again."
          onRetry={() => void ports.refetch()}
        />
      </Card>
    );
  }

  if (rows.length === 0) {
    return (
      <Card title="Ports">
        <EmptyState
          title="No ports recorded."
          action="Walk this device over SNMP to read its interface table, then read its neighbours and clients."
        />
      </Card>
    );
  }

  const selected = expanded === null ? undefined : rows.find((row) => row.ifIndex === expanded);

  return (
    <div className="space-y-5">
      <Card title={`Ports (${rows.length.toString()})`}>
        <div role="table" aria-label="Ports" className="flex flex-col">
          <div
            role="row"
            className="flex items-center gap-4 border-b border-subtle py-2 text-table-header text-muted"
          >
            <span role="columnheader" className="w-16 shrink-0">
              Index
            </span>
            <span role="columnheader" className="w-40 shrink-0">
              Name
            </span>
            <span role="columnheader" className="w-28 shrink-0">
              Role
            </span>
            <span role="columnheader" className="w-28 shrink-0">
              Status
            </span>
            <span role="columnheader" className="min-w-0 flex-1">
              Connected to
            </span>
          </div>

          <div ref={scroller} className="h-[50vh] overflow-auto" data-testid="port-scroller">
            <div
              className="relative w-full"
              style={{ height: `${virtualizer.getTotalSize().toString()}px` }}
            >
              {items.map((item) => {
                const row = rows[item.index];

                if (row === undefined) {
                  return null;
                }

                const isOpen = expanded === row.ifIndex;

                return (
                  <div
                    key={row.ifIndex}
                    role="row"
                    className="absolute top-0 left-0 flex w-full items-center gap-4 border-b border-subtle text-table-cell text-secondary hover:bg-raised"
                    style={{
                      height: `${rowHeight.toString()}px`,
                      transform: `translateY(${item.start.toString()}px)`,
                    }}
                  >
                    <span role="cell" className="w-16 shrink-0 tabular-nums">
                      {row.ifIndex}
                    </span>
                    <span role="cell" className="w-40 shrink-0 truncate font-mono text-primary">
                      {row.name ?? row.description ?? '—'}
                    </span>
                    <span role="cell" className="w-28 shrink-0">
                      <Badge tone={portRoleTones[row.role]}>{portRoleLabels[row.role]}</Badge>
                    </span>
                    <span role="cell" className="w-28 shrink-0 text-muted">
                      {row.interfaceKnown ? interfaceStatusLabels[row.operStatus] : '—'}
                    </span>
                    <span role="cell" className="flex min-w-0 flex-1 items-center gap-3">
                      <span className="min-w-0 flex-1 truncate">{portOccupancySummary(row)}</span>
                      <button
                        type="button"
                        className="shrink-0 rounded-lg px-2 py-1 text-secondary hover:bg-raised hover:text-primary focus-visible:ring-2 focus-visible:ring-accent focus-visible:outline-none"
                        aria-expanded={isOpen}
                        // The visible word is the same on every row, so the name has to carry the
                        // port: a screen reader hearing "Detail, Detail, Detail" down a
                        // forty-eight port switch learns nothing about which one it is on.
                        aria-label={`Detail for port ${row.name ?? row.ifIndex.toString()}`}
                        onClick={() => {
                          setExpanded(isOpen ? null : row.ifIndex);
                        }}
                      >
                        {isOpen ? 'Hide detail' : 'Detail'}
                      </button>
                    </span>
                  </div>
                );
              })}
            </div>
          </div>
        </div>
      </Card>

      {selected === undefined ? null : <PortDetail port={selected} />}
    </div>
  );
}

/**
 * One port in full: why it was classified as it was, what announced itself, and what was learned.
 *
 * A panel under the table rather than a modal, because the reader is comparing it against the
 * rows above — "why is this one an uplink and that one not" is the question, and a dialog would
 * hide the comparison.
 */
function PortDetail({ port }: { readonly port: Port }) {
  const name = port.name ?? port.description ?? `Index ${port.ifIndex.toString()}`;

  return (
    <Card title={`Port ${name}`}>
      <div className="space-y-5">
        <p className="text-body text-secondary">
          <Badge tone={portRoleTones[port.role]}>{portRoleLabels[port.role]}</Badge>{' '}
          <span className="ml-2">{portRoleExplanation(port)}</span>
        </p>

        {port.interfaceKnown ? null : (
          <p className="text-body text-muted">
            No walk has recorded this interface. The port is listed because something was observed
            on it.
          </p>
        )}

        <section aria-labelledby={`neighbours-${port.ifIndex.toString()}`}>
          <h3 id={`neighbours-${port.ifIndex.toString()}`} className="text-card-title text-primary">
            Announced
          </h3>

          {port.neighbors.length === 0 ? (
            <p className="mt-2 text-body text-muted">
              Nothing on this port announced itself over LLDP or CDP.
            </p>
          ) : (
            <ul className="mt-2 space-y-3">
              {port.neighbors.map((neighbor) => (
                <li key={neighbor.adjacencyId} className="rounded-xl bg-raised p-3">
                  <div className="flex flex-wrap items-center gap-2">
                    <span className="text-body text-primary">
                      {neighbor.deviceId === null ? (
                        (neighbor.systemName ?? neighbor.chassisId)
                      ) : (
                        <Link
                          to="/devices/$deviceId"
                          params={{ deviceId: neighbor.deviceId }}
                          className="text-accent hover:underline"
                        >
                          {neighbor.hostname ?? neighbor.systemName ?? neighbor.chassisId}
                        </Link>
                      )}
                    </span>

                    <Badge tone={neighbor.managed ? 'accent' : 'muted'}>
                      {neighbor.managed ? 'Monitored' : 'Not monitored'}
                    </Badge>

                    {neighbor.capabilities.map((capability) => (
                      <Badge key={capability} tone="violet">
                        {capabilityLabels[capability]}
                      </Badge>
                    ))}
                  </div>

                  {neighbor.systemDescription === null ? null : (
                    <p className="mt-1 text-body text-secondary">{neighbor.systemDescription}</p>
                  )}

                  <dl className="mt-2 grid grid-cols-2 gap-x-4 gap-y-1 text-table-cell text-muted sm:grid-cols-4">
                    <div>
                      <dt className="inline">Far port: </dt>
                      <dd className="inline font-mono text-secondary">
                        {neighbor.remotePortName ?? '—'}
                      </dd>
                    </div>
                    <div>
                      <dt className="inline">Confidence: </dt>
                      <dd className="inline text-secondary">
                        {confidenceLabels[neighbor.confidence]}
                      </dd>
                    </div>
                    <div>
                      <dt className="inline">Seen by: </dt>
                      <dd className="inline text-secondary">
                        {neighbor.sources.map((source) => neighborSourceLabels[source]).join(', ')}
                        {neighbor.bidirectional ? ' (both ends)' : ' (one end)'}
                      </dd>
                    </div>
                    <div>
                      <dt className="inline">Last seen: </dt>
                      <dd className="inline text-secondary">
                        <Timestamp value={neighbor.lastSeenAt} />
                      </dd>
                    </div>
                  </dl>
                </li>
              ))}
            </ul>
          )}
        </section>

        <section aria-labelledby={`hosts-${port.ifIndex.toString()}`}>
          <h3 id={`hosts-${port.ifIndex.toString()}`} className="text-card-title text-primary">
            Learned addresses
          </h3>

          {port.clientsListed ? (
            port.clients.length === 0 ? (
              <p className="mt-2 text-body text-muted">
                No addresses have been learned on this port.
              </p>
            ) : (
              <ul className="mt-2 space-y-2">
                {port.clients.map((client) => (
                  <li
                    key={client.clientId}
                    className="flex flex-wrap items-center gap-3 text-table-cell"
                  >
                    <Link
                      to="/clients/$clientId"
                      params={{ clientId: client.clientId }}
                      className="font-mono text-accent hover:underline"
                    >
                      {client.macAddress}
                    </Link>
                    <span className="font-mono text-secondary">{client.ipAddress ?? '—'}</span>
                    <span className="text-muted">
                      {client.vlanId === null ? 'No VLAN' : `VLAN ${client.vlanId.toString()}`}
                    </span>
                    <span className="font-mono text-muted">{client.oui}</span>
                    <span className="text-muted">
                      <Timestamp value={client.lastSeenAt} />
                    </span>
                  </li>
                ))}
              </ul>
            )
          ) : (
            /* The heart of the package. These addresses are real and they are not on this cable. */
            <p className="mt-2 text-body text-secondary">
              This port carries {(port.learnedAddressCount ?? port.clientCount).toString()}{' '}
              addresses for what is behind it, so they are counted rather than listed. A MAC address
              is learned by every switch on the path to it, and showing them here would say they are
              plugged in at this port.
            </p>
          )}
        </section>
      </div>
    </Card>
  );
}
