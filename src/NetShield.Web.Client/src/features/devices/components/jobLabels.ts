import type { Schemas } from '@/api/types';
import type { BadgeTone } from '@/components/ui/Badge';
import type { DeviceWalk } from '@/features/devices/api/deviceMutations';

/**
 * What each job status is called on screen, and which semantic colour carries it.
 *
 * DESIGN.md §1 admits no hue that does not encode state, and these do: a job waiting is
 * informational, a job running is interactive-in-progress, a finished one is healthy, a failed
 * one is a failure, and a withdrawn one is neither good nor bad — it is muted, the way an
 * `Unknown` device state is, because nothing went wrong and nothing was learned.
 */
export const jobStatusLabels: Record<Schemas['CollectorJobStatus'], string> = {
  Pending: 'Queued',
  Leased: 'Running',
  Succeeded: 'Succeeded',
  Failed: 'Failed',
  Cancelled: 'Cancelled',
};

export const jobStatusTones: Record<Schemas['CollectorJobStatus'], BadgeTone> = {
  Pending: 'info',
  Leased: 'accent',
  Succeeded: 'success',
  Failed: 'danger',
  Cancelled: 'muted',
};

/**
 * What each job kind is called. `Discover` covers five different reads, which is why the walk
 * discriminator is shown beside it rather than instead of it.
 */
export const jobKindLabels: Record<Schemas['CollectorJobKind'], string> = {
  Poll: 'Reachability poll',
  Discover: 'Discovery',
  ConfigFetch: 'Configuration fetch',
};

/**
 * What each walk discriminator is called. The wire carries the collector's own vocabulary —
 * `snmp`, `clients`, `neighbors` — and none of those is a sentence-case label (DESIGN.md §4).
 */
const walkLabels: Record<string, string> = {
  snmp: 'Fingerprint',
  sweep: 'Address sweep',
  clients: 'Clients',
  neighbors: 'Neighbours',
  routes: 'Routes',
  vlans: 'VLANs',
};

/**
 * A job in one phrase: what it is, and which read it is where the kind alone does not say.
 *
 * A walk the client does not recognise is shown as itself rather than dropped. It means the API
 * grew a walk this build of the SPA predates, and the honest thing is to show what the server
 * said rather than to render a `Discovery` row that looks identical to four others.
 */
export function describeJob(
  kind: Schemas['CollectorJobKind'],
  walk: string | null | undefined,
): string {
  if (walk === null || walk === undefined || walk === '') {
    return jobKindLabels[kind];
  }

  return walkLabels[walk] ?? walk;
}

/** The walks a person can ask for from the device screen, in the order they are offered. */
export const walkActions: readonly { readonly walk: DeviceWalk; readonly label: string }[] = [
  { walk: 'fingerprint', label: 'Fingerprint' },
  { walk: 'neighbors', label: 'Neighbours' },
  { walk: 'routes', label: 'Routes' },
  { walk: 'vlans', label: 'VLANs' },
  { walk: 'clients', label: 'Clients' },
];
