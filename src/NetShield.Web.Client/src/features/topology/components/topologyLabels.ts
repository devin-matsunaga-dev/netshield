import {
  BrickWall,
  Network,
  Router,
  Scale,
  Server,
  Share2,
  Wifi,
  type LucideIcon,
} from 'lucide-react';

import type { Schemas } from '@/api/types';
// Imported rather than redefined: WP-1.7 wrote the role names for the device table, and two
// tables of one enum's labels are the beginning of two different names for one thing.
import { roleLabels, vendorLabels } from '@/features/devices/components/deviceLabels';

/**
 * The icon a node tile carries, by what the device is for.
 *
 * DESIGN.md §6 makes a topology node a 48px icon tile and the reference screenshot draws a
 * different glyph for a firewall, a switch and an access point; §9.2 is the reason it matters
 * beyond decoration — the border colour is the state, and a colour is never allowed to be the
 * only signal, so the tile also says what the thing is. Lucide, per DESIGN.md §2.
 */
export const roleIcons: Record<Schemas['DeviceRole'], LucideIcon> = {
  Router: Router,
  Switch: Network,
  Firewall: BrickWall,
  AccessPoint: Wifi,
  LoadBalancer: Scale,
  Server: Server,
  Other: Share2,
};

/**
 * How a node tile is dressed, by state — DESIGN.md §3's mapping, which is fixed and "used
 * everywhere without exception": Online → success, Warning → warning, Offline → danger,
 * Unknown → text-muted.
 *
 * DESIGN.md §6 says the *border* encodes the state, and the reference screenshot's topology card
 * draws the tiles filled in that state's colour as well. Both are honoured, the way §6's KPI
 * icon tile already does it — the 12%-alpha tint behind the solid semantic mark — because the
 * screenshot wins where the two disagree and neither reading introduces a colour outside the
 * token table.
 *
 * Unknown has no hue of its own in §3, so it is the raised surface and a muted mark, which is
 * the same answer `Badge` reached.
 *
 * Written as whole class names rather than composed, because Tailwind reads the source for the
 * classes it emits and a name built at runtime is one it never sees.
 */
export const stateTiles: Record<Schemas['DeviceState'], string> = {
  Online: 'border-success bg-success-tint text-success',
  Warning: 'border-warning bg-warning-tint text-warning',
  Offline: 'border-danger bg-danger-tint text-danger',
  Unknown: 'border-strong bg-raised text-muted',
};

/**
 * The same mapping as the *text* colour a dot inherits — the dot itself is `bg-current`, so a
 * 6px filled dot in the semantic colour (DESIGN.md §6's severity indicator) needs no background
 * token that DESIGN.md §3 does not name. Used by the legend and the table's state column.
 */
export const stateTones: Record<Schemas['DeviceState'], string> = {
  Online: 'text-success',
  Warning: 'text-warning',
  Offline: 'text-danger',
  Unknown: 'text-muted',
};

/** What each state is called on screen. Sentence case, like every other label (DESIGN.md §4). */
export const stateLabels: Record<Schemas['DeviceState'], string> = {
  Online: 'Online',
  Warning: 'Warning',
  Offline: 'Offline',
  Unknown: 'Unknown',
};

/**
 * How much an edge is worth, in words.
 *
 * WP-2.1 settled three levels decided by whether a neighbour protocol saw the edge and whether
 * both ends reported it. They are not drawn — DESIGN.md §6 gives every edge one appearance and
 * §9 admits no invented visual direction — so this is where they are said instead: in the
 * accessible name of the link and in the table fallback.
 */
export const confidenceLabels: Record<Schemas['AdjacencyConfidence'], string> = {
  Confirmed: 'Confirmed',
  Probable: 'Probable',
  Possible: 'Possible',
};

/** What supported the edge. `Routing` is a next hop, which is a layer-3 claim about a port. */
export const sourceLabels: Record<Schemas['NeighborSource'], string> = {
  Lldp: 'LLDP',
  Cdp: 'CDP',
  Routing: 'Routing',
};

/** The protocols behind an edge, as a reader would write them. */
export function formatSources(sources: readonly Schemas['NeighborSource'][]): string {
  return sources.length === 0 ? '—' : sources.map((source) => sourceLabels[source]).join(', ');
}

/** One end of a link, as a port: its name where a walk read one, its index otherwise. */
export function formatPort(name: string | null, ifIndex: number | null): string {
  if (name !== null && name.length > 0) {
    return name;
  }

  return ifIndex === null ? '—' : `Interface ${ifIndex.toString()}`;
}

/**
 * The accessible name of a link, which carries everything the drawn edge deliberately does not:
 * which ports, which protocols saw it, how much it is worth, and whether both ends agreed.
 */
export function describeEdge(
  aHostname: string,
  aPort: string,
  bHostname: string,
  bPort: string,
  confidence: Schemas['AdjacencyConfidence'],
  bidirectional: boolean,
  sources: readonly Schemas['NeighborSource'][],
): string {
  const agreement = bidirectional ? 'both ends reported it' : 'only one end reported it';

  return (
    `${aHostname} ${aPort} to ${bHostname} ${bPort}. ` +
    `${confidenceLabels[confidence]}, ${agreement}, from ${formatSources(sources)}.`
  );
}

/**
 * What a tile says under its label. The role, and — where the device has links leading out of
 * the monitored estate — how many.
 *
 * WP-2.3 settled that a node is a device, so an edge to an unmanaged switch or an LLDP-speaking
 * server is counted rather than drawn. Without this a switch with an uplink to something
 * NetShield does not monitor shows two cables and reports three, which reads as a missing edge
 * rather than as the deliberate absence it is.
 */
export function nodeSubLabel(role: Schemas['DeviceRole'], externalEdgeCount: number): string {
  const label = roleLabels[role];

  return externalEdgeCount === 0 ? label : `${label} · +${externalEdgeCount.toString()}`;
}

/** The long form of the same, for the tile's accessible description. */
export function describeExternalEdges(externalEdgeCount: number): string {
  if (externalEdgeCount === 0) {
    return '';
  }

  const links = externalEdgeCount === 1 ? '1 link' : `${externalEdgeCount.toString()} links`;

  return ` ${links} to something NetShield does not monitor, which the map cannot draw.`;
}

export { roleLabels, vendorLabels };
