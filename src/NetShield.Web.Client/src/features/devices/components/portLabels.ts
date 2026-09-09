import type { Schemas } from '@/api/types';
import type { BadgeTone } from '@/components/ui/Badge';

/**
 * What a port's classification is called on screen, and how it is said in a sentence.
 *
 * The rule these label is deliberately arguable — an uplink is a reading of evidence rather than
 * something the network states — so the screen has to say what the reading was and what produced
 * it. A badge saying "Uplink" with nothing beside it is a verdict a reader cannot check.
 */
export const portRoleLabels: Record<Schemas['PortRole'], string> = {
  Empty: 'Empty',
  Access: 'Access',
  Uplink: 'Uplink',
};

/**
 * How a role renders. `accent` for an uplink because it is interactive-blue's other job,
 * informational — an uplink is not a fault and not a success, and DESIGN.md §3 admits no hue
 * that means "structural". `Empty` sits on `muted` for the same reason a null does.
 */
export const portRoleTones: Record<Schemas['PortRole'], BadgeTone> = {
  Empty: 'muted',
  Access: 'success',
  Uplink: 'accent',
};

/** What each capability bit means, in words a person uses. */
export const capabilityLabels: Record<Schemas['SystemCapability'], string> = {
  Other: 'Other',
  Repeater: 'Repeater',
  Bridge: 'Switch',
  WlanAccessPoint: 'Access point',
  Router: 'Router',
  Telephone: 'IP phone',
  DocsisCableDevice: 'Cable device',
  Station: 'Workstation',
};

export const confidenceLabels: Record<Schemas['AdjacencyConfidence'], string> = {
  Confirmed: 'Confirmed',
  Probable: 'Probable',
  Possible: 'Possible',
};

export const neighborSourceLabels: Record<Schemas['NeighborSource'], string> = {
  Lldp: 'LLDP',
  Cdp: 'CDP',
  Routing: 'Routing',
};

/**
 * Why the port was classified as it was, as a sentence a reader can act on.
 *
 * `LearnedAddressCount` names the number rather than the threshold, because the number is the
 * evidence and the threshold is a setting — an operator who disagrees needs to know the port
 * carries two hundred addresses, not that eight is the cut-off.
 */
export function portRoleExplanation(port: {
  readonly role: Schemas['PortRole'];
  readonly roleReason: Schemas['PortRoleReason'];
  readonly learnedAddressCount: number | null;
  readonly clientCount: number;
}): string {
  switch (port.roleReason) {
    case 'ManagedDevice':
      return 'Faces a monitored device.';
    case 'InfrastructureNeighbor':
      return 'The far end reports itself as network infrastructure.';
    case 'LearnedAddressCount': {
      const carried = port.learnedAddressCount ?? port.clientCount;

      return `Carries ${carried.toString()} ${carried === 1 ? 'address' : 'addresses'} for what is behind it.`;
    }
    case 'Endpoints':
      return 'Endpoints are attached here.';
    case 'NoEvidence':
      return 'Nothing has been observed on this port.';
  }
}

/**
 * What a port's occupants amount to, for the cell a reader scans down.
 *
 * An uplink says a count and never a list, which is the whole distinction WP-2.6 exists to draw:
 * a MAC address is learned by every bridge on the path to it, so listing an uplink's addresses
 * would say two hundred hosts are plugged into one cable.
 */
export function portOccupancySummary(port: {
  readonly role: Schemas['PortRole'];
  readonly clientsListed: boolean;
  readonly clientCount: number;
  readonly learnedAddressCount: number | null;
  readonly neighbors: readonly {
    readonly hostname: string | null;
    readonly systemName: string | null;
  }[];
}): string {
  const named = port.neighbors
    .map((neighbor) => neighbor.hostname ?? neighbor.systemName)
    .filter((name): name is string => name !== null);

  if (!port.clientsListed) {
    const carried = port.learnedAddressCount ?? port.clientCount;
    const addresses = `${carried.toString()} ${carried === 1 ? 'address' : 'addresses'}`;

    return named.length > 0
      ? `${named.join(', ')} — carrying ${addresses}`
      : `Carrying ${addresses}`;
  }

  const hosts =
    port.clientCount === 0
      ? []
      : [`${port.clientCount.toString()} ${port.clientCount === 1 ? 'host' : 'hosts'}`];

  const parts = [...named, ...hosts];

  return parts.length === 0 ? 'Nothing observed' : parts.join(', ');
}
