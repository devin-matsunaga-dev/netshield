import { Panel, useReactFlow } from '@xyflow/react';
import { Maximize, Minus, Plus } from 'lucide-react';
import type { ReactNode } from 'react';

/**
 * Zoom in, zoom out and fit, stacked vertically at the top-left of the canvas as 32px
 * `bg-raised` buttons — DESIGN.md §6, and where the reference screenshot's topology card puts
 * them.
 *
 * Written rather than React Flow's own `<Controls>`: that one ships an interactivity lock this
 * canvas has nothing to lock, its own palette, and a fourth button. These are three, in the
 * tokens, each with an `aria-label` because they are icon-only (CONVENTIONS.md §6).
 */
export function TopologyControls() {
  const { zoomIn, zoomOut, fitView } = useReactFlow();

  // Inset from the canvas edge rather than flush to it, as the reference card draws them.
  return (
    <Panel position="top-left" className="m-3 flex flex-col gap-1">
      <ControlButton label="Zoom in" onClick={() => void zoomIn()}>
        <Plus size={16} aria-hidden="true" />
      </ControlButton>
      <ControlButton label="Zoom out" onClick={() => void zoomOut()}>
        <Minus size={16} aria-hidden="true" />
      </ControlButton>
      <ControlButton label="Fit the map to the view" onClick={() => void fitView()}>
        <Maximize size={16} aria-hidden="true" />
      </ControlButton>
    </Panel>
  );
}

interface ControlButtonProps {
  readonly label: string;
  readonly onClick: () => void;
  readonly children: ReactNode;
}

function ControlButton({ label, onClick, children }: ControlButtonProps) {
  return (
    <button
      type="button"
      aria-label={label}
      title={label}
      onClick={onClick}
      className="flex h-canvas-control w-canvas-control items-center justify-center rounded-control border border-subtle bg-raised text-secondary transition-colors duration-hover hover:border-strong hover:text-primary focus-visible:ring-2 focus-visible:ring-accent focus-visible:outline-none"
    >
      {children}
    </button>
  );
}
