import type { ReactNode } from 'react';

interface CardProps {
  readonly title?: string;
  readonly children: ReactNode;
}

/**
 * A card (DESIGN.md §6): the panel surface, a hairline border, 12px radius, no shadow. A 48px
 * header row when it is titled, 20px of body padding. Cards do not nest.
 */
export function Card({ title, children }: CardProps) {
  return (
    <section className="rounded-card border border-subtle bg-surface">
      {title !== undefined && (
        <div className="flex h-card-header items-center border-b border-subtle px-gutter">
          <h2 className="text-card-title text-primary">{title}</h2>
        </div>
      )}
      <div className="p-gutter">{children}</div>
    </section>
  );
}
