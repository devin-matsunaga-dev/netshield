import type { ReactNode } from 'react';

interface EmptyStateProps {
  /** The situation, stated plainly. */
  readonly title: string;
  /** What to do next. DESIGN.md §8 asks for this, and it is the half people leave out. */
  readonly action: string;
  /** An optional control that performs it. */
  readonly children?: ReactNode;
}

/**
 * Nothing to show, and what to do about it (DESIGN.md §8).
 *
 * "No devices yet. Run discovery or add one manually." — the situation, then the next step. It
 * never apologises and never says "Oops".
 */
export function EmptyState({ title, action, children }: EmptyStateProps) {
  return (
    <div className="flex flex-col items-center gap-3 px-content py-12 text-center">
      <p className="text-body text-primary">{title}</p>
      <p className="text-metric-caption text-muted">{action}</p>
      {children}
    </div>
  );
}
