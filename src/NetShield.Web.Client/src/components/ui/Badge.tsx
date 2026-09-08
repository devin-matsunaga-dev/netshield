import type { ReactNode } from 'react';

import { cn } from '@/lib/cn';

/**
 * The semantic roles a badge can carry. Every one of them encodes state — DESIGN.md §1 says a
 * surface is never tinted for decoration, so there is deliberately no neutral "grey badge for
 * things that are fine".
 */
export type BadgeTone = 'accent' | 'success' | 'warning' | 'danger' | 'info' | 'violet' | 'muted';

interface BadgeProps {
  readonly tone: BadgeTone;
  readonly children: ReactNode;
}

/**
 * A badge (DESIGN.md §6): a pill at 11px/500, 2px of vertical and 8px of horizontal padding, the
 * semantic tint behind the solid semantic text.
 *
 * The label is always the child. DESIGN.md §9.2 admits no colour as the only signal, so a badge
 * with no text in it is not a badge — it is a coloured dot pretending to mean something.
 */
const tones: Record<BadgeTone, string> = {
  accent: 'bg-accent-tint text-accent',
  success: 'bg-success-tint text-success',
  warning: 'bg-warning-tint text-warning',
  danger: 'bg-danger-tint text-danger',
  info: 'bg-info-tint text-info',
  violet: 'bg-violet-tint text-violet',
  // Unknown has no hue of its own in DESIGN.md §3 — it is `text-muted` on the raised surface.
  muted: 'bg-raised text-muted',
};

export function Badge({ tone, children }: BadgeProps) {
  return (
    <span
      className={cn(
        'inline-flex items-center rounded-full px-2 py-0.5 text-badge whitespace-nowrap',
        tones[tone],
      )}
    >
      {children}
    </span>
  );
}
