import type { Schemas } from '@/api/types';
import type { StateCounts } from '@/features/topology/api/topologyGraph';
import { stateLabels, stateTones } from '@/features/topology/components/topologyLabels';
import { cn } from '@/lib/cn';

/**
 * The three states the reference screenshot's topology card legends — Online, Warning, Offline.
 * The screenshot is the source of truth and it shows exactly these (DESIGN.md, preamble).
 */
const always: readonly Schemas['DeviceState'][] = ['Online', 'Warning', 'Offline'];

interface TopologyLegendProps {
  readonly stateCounts: StateCounts;
}

/**
 * The state legend, in the card header rather than on the canvas (DESIGN.md §6).
 *
 * Unknown is drawn only when the graph actually holds one. `DeviceState` has four members and
 * the reference card legends three, and both readings matter: the screenshot is law, and a tile
 * whose border is a colour with no entry beside it is a colour encoding nothing to the reader.
 * Showing it conditionally keeps the reference's picture for the estate the reference describes
 * and stays honest about an estate with a device nothing has reached yet.
 */
export function TopologyLegend({ stateCounts }: TopologyLegendProps) {
  const states = stateCounts.Unknown > 0 ? [...always, 'Unknown' as const] : always;

  return (
    <ul aria-label="Device state" className="flex items-center gap-4">
      {states.map((state) => (
        <li key={state} className={cn('flex items-center gap-2', stateTones[state])}>
          <span className="h-1.5 w-1.5 rounded-full bg-current" aria-hidden="true" />
          <span className="text-metric-caption">{stateLabels[state]}</span>
        </li>
      ))}
    </ul>
  );
}
