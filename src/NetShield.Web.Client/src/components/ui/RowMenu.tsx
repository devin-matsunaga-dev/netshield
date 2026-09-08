import { useEffect, useRef, useState, type ReactNode } from 'react';

import { cn } from '@/lib/cn';

interface RowMenuProps {
  /** Names what the menu acts on, so "More actions" is not the whole label a reader hears. */
  readonly label: string;
  readonly children: ReactNode;
}

/**
 * The trailing 40px column of a table row (DESIGN.md §6): a vertical-dots control that opens the
 * actions for that row.
 *
 * The dots are a decorative glyph with an `aria-label` on the button beside them
 * (CONVENTIONS.md §6 wants one on every icon-only control), and the label names the row rather
 * than saying "More" — a screen reader moving down a table would otherwise hear the same three
 * words five hundred times.
 *
 * Escape closes it, a click outside closes it, and the items inside are ordinary links and
 * buttons in the tab order.
 */
export function RowMenu({ label, children }: RowMenuProps) {
  const [open, setOpen] = useState(false);
  const container = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) {
      return;
    }

    function onPointerDown(event: MouseEvent) {
      if (container.current !== null && !container.current.contains(event.target as Node)) {
        setOpen(false);
      }
    }

    function onKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') {
        setOpen(false);
      }
    }

    document.addEventListener('mousedown', onPointerDown);
    document.addEventListener('keydown', onKeyDown);

    return () => {
      document.removeEventListener('mousedown', onPointerDown);
      document.removeEventListener('keydown', onKeyDown);
    };
  }, [open]);

  return (
    <div ref={container} className="relative">
      <button
        type="button"
        aria-label={label}
        aria-expanded={open}
        aria-haspopup="menu"
        onClick={(event) => {
          // The row around this is a link. Without both, opening the menu navigates instead.
          event.preventDefault();
          event.stopPropagation();
          setOpen((current) => !current);
        }}
        className={cn(
          'flex h-8 w-8 items-center justify-center rounded-control text-muted',
          'transition-colors duration-hover hover:bg-raised hover:text-primary',
          'focus-visible:ring-2 focus-visible:ring-accent focus-visible:outline-none',
        )}
      >
        <span aria-hidden="true">⋮</span>
      </button>
      {open && (
        <div
          role="menu"
          className="absolute right-0 z-10 mt-1 min-w-40 rounded-control border border-subtle bg-surface py-1"
          onClick={() => {
            setOpen(false);
          }}
        >
          {children}
        </div>
      )}
    </div>
  );
}

/** One action inside a {@link RowMenu}. A button, because the menu closes when it is chosen. */
export function RowMenuItem({
  onSelect,
  children,
}: {
  readonly onSelect: () => void;
  readonly children: ReactNode;
}) {
  return (
    <button
      type="button"
      role="menuitem"
      onClick={(event) => {
        event.preventDefault();
        event.stopPropagation();
        onSelect();
      }}
      className="block w-full px-3 py-1.5 text-left text-table-cell text-secondary transition-colors duration-hover hover:bg-raised hover:text-primary focus-visible:ring-2 focus-visible:ring-accent focus-visible:outline-none"
    >
      {children}
    </button>
  );
}
