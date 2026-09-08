import type { Schemas } from '@/api/types';
import { Badge, type BadgeTone } from '@/components/ui/Badge';

/**
 * The device-state mapping DESIGN.md §3 fixes, and the only place it is written:
 * Online → success, Warning → warning, Offline → danger, Unknown → text-muted.
 *
 * It is stated to be used "everywhere without exception", so a second copy of it somewhere else
 * would be the beginning of the exception.
 */
const tones: Record<Schemas['DeviceState'], BadgeTone> = {
  Online: 'success',
  Warning: 'warning',
  Offline: 'danger',
  Unknown: 'muted',
};

/** What each state is called on screen. Sentence case, like every other label (DESIGN.md §4). */
const labels: Record<Schemas['DeviceState'], string> = {
  Online: 'Online',
  Warning: 'Warning',
  Offline: 'Offline',
  Unknown: 'Unknown',
};

export function DeviceStateBadge({ state }: { readonly state: Schemas['DeviceState'] }) {
  return <Badge tone={tones[state]}>{labels[state]}</Badge>;
}
