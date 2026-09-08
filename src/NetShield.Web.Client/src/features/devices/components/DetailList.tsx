import type { ReactNode } from 'react';

/**
 * A two-column list of facts — the shape every detail panel on the device screen uses.
 *
 * A real `<dl>`, so each label is bound to its value for anything reading the page that is not a
 * pair of eyes. An absent value is an em dash rather than a blank: "nothing answered this" and
 * "nobody has rendered this" look identical otherwise.
 */
export function DetailList({ children }: { readonly children: ReactNode }) {
  return <dl className="grid grid-cols-1 gap-x-gutter gap-y-3 sm:grid-cols-2">{children}</dl>;
}

interface DetailRowProps {
  readonly label: string;
  /** Addresses, serials and MACs are monospace (DESIGN.md §4). Prose is not. */
  readonly mono?: boolean;
  readonly children?: ReactNode;
}

export function DetailRow({ label, mono = false, children }: DetailRowProps) {
  const empty = children === null || children === undefined || children === '';

  return (
    <div>
      <dt className="text-metric-caption text-muted">{label}</dt>
      <dd
        className={mono && !empty ? 'font-mono text-body text-primary' : 'text-body text-primary'}
      >
        {empty ? <span className="text-muted">—</span> : children}
      </dd>
    </div>
  );
}
