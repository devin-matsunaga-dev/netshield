import type { ReactNode } from 'react';

interface CardProps {
  readonly title?: string;
  /**
   * The optional control DESIGN.md §6 puts at the right of the header row — a "View All" ghost
   * button, a unit dropdown, or a legend. Only meaningful beside a title, because the header row
   * only exists when there is one.
   */
  readonly control?: ReactNode;
  readonly children: ReactNode;
}

/**
 * A card (DESIGN.md §6): the panel surface, a hairline border, 12px radius, no shadow. A 48px
 * header row when it is titled, 20px of body padding. Cards do not nest.
 */
export function Card({ title, control, children }: CardProps) {
  return (
    <section className="rounded-card border border-subtle bg-surface">
      {title !== undefined && (
        <div className="flex h-card-header items-center justify-between gap-4 border-b border-subtle px-gutter">
          <h2 className="text-card-title text-primary">{title}</h2>
          {control}
        </div>
      )}
      <div className="p-gutter">{children}</div>
    </section>
  );
}
