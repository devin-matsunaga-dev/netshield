import { cn } from '@/lib/cn';

export interface TabDefinition {
  readonly id: string;
  readonly label: string;
}

interface TabsProps {
  readonly label: string;
  readonly tabs: readonly TabDefinition[];
  readonly active: string;
  readonly onChange: (id: string) => void;
}

/**
 * A tab strip, built to the nav-item rules in DESIGN.md §6 — 40px tall, 8px radius, secondary at
 * rest, the accent tint and primary text when active.
 *
 * It is a real ARIA tablist: arrow keys move between tabs, and only the active one is in the tab
 * order, which is what a reader using a keyboard expects of a tab strip and what they get from
 * nothing else. The panel is rendered by the caller and points back with `aria-labelledby`.
 */
export function Tabs({ label, tabs, active, onChange }: TabsProps) {
  function move(direction: 1 | -1) {
    const index = tabs.findIndex((tab) => tab.id === active);
    const next = tabs[(index + direction + tabs.length) % tabs.length];

    if (next !== undefined) {
      onChange(next.id);
    }
  }

  return (
    <div
      role="tablist"
      aria-label={label}
      className="flex gap-1 border-b border-subtle"
      onKeyDown={(event) => {
        if (event.key === 'ArrowRight') {
          event.preventDefault();
          move(1);
        } else if (event.key === 'ArrowLeft') {
          event.preventDefault();
          move(-1);
        }
      }}
    >
      {tabs.map((tab) => {
        const selected = tab.id === active;

        return (
          <button
            key={tab.id}
            type="button"
            role="tab"
            id={`tab-${tab.id}`}
            aria-selected={selected}
            aria-controls={`panel-${tab.id}`}
            tabIndex={selected ? 0 : -1}
            onClick={() => {
              onChange(tab.id);
            }}
            className={cn(
              'h-nav-item rounded-t-control px-4 text-nav-item transition-colors duration-hover',
              'focus-visible:ring-2 focus-visible:ring-accent focus-visible:outline-none',
              selected
                ? 'bg-accent-tint text-primary'
                : 'text-secondary hover:bg-raised hover:text-primary',
            )}
          >
            {tab.label}
          </button>
        );
      })}
    </div>
  );
}
