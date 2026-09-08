import type { Schemas } from '@/api/types';
import type { SeenWindow } from '@/features/clients/api/clientFilters';

/**
 * What each enum member is called on screen.
 *
 * The wire carries a name — `"ArpTable"` — because WP-0.4 settled that an enum travels as its
 * name so a member inserted later cannot renumber what a stored response meant. A name is not a
 * label, though: sentence case is the rule (DESIGN.md §4). This table is where the two are
 * reconciled, the same arrangement `deviceLabels.ts` uses.
 */
export const observationSourceLabels: Record<Schemas['ClientObservationSource'], string> = {
  ArpTable: 'ARP table',
  MacAddressTable: 'MAC address table',
  DhcpLease: 'DHCP lease',
  WirelessAssociation: 'Wireless association',
};

export const assetKindLabels: Record<Schemas['AssetKind'], string> = {
  Device: 'Device',
  Client: 'Client',
  Unresolved: 'Nothing',
};

export const seenWindowLabels: Record<SeenWindow, string> = {
  '1h': 'Last hour',
  '24h': 'Last 24 hours',
  '7d': 'Last 7 days',
  '30d': 'Last 30 days',
};

/**
 * What an OUI stands for, as far as NetShield can honestly say.
 *
 * The IEEE registry is not in this repository — its licence, its size and its update cadence
 * deserve a decision of their own rather than a casual commit — so the prefix is shown as itself.
 * A locally administered address is different in kind rather than merely unresolved: nobody
 * registered it, so there is no vendor for a registry to name, and that is what MAC randomisation
 * on a phone looks like. Saying so is more use than showing three octets that stand for nothing.
 */
export function formatOui(oui: string, locallyAdministered: boolean): string {
  return locallyAdministered ? 'Locally administered' : oui;
}

/**
 * How long a binding has been open, or how long it lasted.
 *
 * Written in the largest unit that still says something: an interval of four days does not need
 * its minutes, and one of nine minutes has nothing else to give.
 */
export function formatDuration(fromIso: string, toIso: string | null | undefined): string {
  const from = Date.parse(fromIso);
  const to = toIso === null || toIso === undefined ? Date.now() : Date.parse(toIso);

  if (Number.isNaN(from) || Number.isNaN(to) || to < from) {
    return '—';
  }

  const seconds = Math.floor((to - from) / 1000);

  if (seconds < 60) {
    return `${seconds.toString()} s`;
  }

  const minutes = Math.floor(seconds / 60);

  if (minutes < 60) {
    return `${minutes.toString()} min`;
  }

  const hours = Math.floor(minutes / 60);

  if (hours < 24) {
    return `${hours.toString()} h`;
  }

  return `${Math.floor(hours / 24).toString()} d`;
}

/**
 * What a port's learned-address count says about the port.
 *
 * Not a guess at the topology — that is Phase 2's — but the evidence read out loud. A port that
 * learned one address is where something is plugged in; a port that learned two hundred is
 * carrying everything behind it.
 */
export function describePort(macCountOnPort: number | null | undefined): string | null {
  if (macCountOnPort === null || macCountOnPort === undefined) {
    return null;
  }

  if (macCountOnPort === 1) {
    return 'Access port — 1 address learned';
  }

  return `${macCountOnPort.toString()} addresses learned on this port`;
}
