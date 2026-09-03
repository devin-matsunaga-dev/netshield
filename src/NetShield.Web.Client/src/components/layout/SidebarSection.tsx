import { Link, useRouterState } from '@tanstack/react-router';
import { ChevronRight } from 'lucide-react';
import { useId, useState } from 'react';

import type { NavEntry } from '@/app/navigation';
import { cn } from '@/lib/cn';

interface SidebarSectionProps {
  readonly entry: NavEntry;
  readonly collapsed: boolean;
  readonly onExpandSidebar: () => void;
  /** Closes the drawer once a destination has been chosen from it (DESIGN.md §5). */
  readonly onNavigate: () => void;
}

/**
 * A sidebar row that expands into destinations (DESIGN.md §6): a right chevron that rotates 90°
 * when open, children indented 32px at 13px. The section itself navigates nowhere — its children
 * are the destinations.
 */
export function SidebarSection({
  entry,
  collapsed,
  onExpandSidebar,
  onNavigate,
}: SidebarSectionProps) {
  const pathname = useRouterState({ select: (state) => state.location.pathname });
  const children = entry.children ?? [];
  const holdsActiveRoute = children.some((child) => pathname.startsWith(child.to));

  // A section holding the active route is open unless the reader has said otherwise, so a link
  // followed from elsewhere leaves the sidebar showing where you are. Derived rather than
  // synchronised: state that follows the route does not need an effect to keep up with it.
  const [override, setOverride] = useState<boolean | null>(null);
  const open = override ?? holdsActiveRoute;
  const panelId = useId();

  const Icon = entry.icon;
  const expanded = open && !collapsed;

  return (
    <li className="relative">
      <button
        type="button"
        aria-expanded={expanded}
        aria-controls={panelId}
        title={collapsed ? entry.label : undefined}
        onClick={() => {
          // Collapsed, there is nowhere for the children to go: open the sidebar first.
          if (collapsed) {
            onExpandSidebar();
            setOverride(true);
            return;
          }

          setOverride(!open);
        }}
        className={cn(
          'flex h-nav-item w-full items-center gap-3 rounded-control px-3',
          'text-nav-item text-secondary transition-colors duration-hover',
          'hover:bg-raised hover:text-primary',
          holdsActiveRoute && 'text-primary',
          collapsed && 'justify-center px-0',
        )}
      >
        {holdsActiveRoute && (
          <span
            aria-hidden
            className="absolute top-1/2 -left-2 h-6 w-[3px] -translate-y-1/2 rounded-r bg-accent"
          />
        )}
        <Icon aria-hidden className="size-5 shrink-0" />
        {!collapsed && (
          <>
            <span className="flex-1 truncate text-left">{entry.label}</span>
            <ChevronRight
              aria-hidden
              className={cn(
                'size-4 shrink-0 transition-transform duration-hover',
                expanded && 'rotate-90',
              )}
            />
          </>
        )}
        {collapsed && <span className="sr-only">{entry.label}</span>}
      </button>

      <ul id={panelId} hidden={!expanded} className="mt-1 space-y-1">
        {children.map((child) => (
          <li key={child.to}>
            <Link
              to={child.to}
              onClick={onNavigate}
              className={cn(
                'flex h-nav-item items-center rounded-control pr-3 pl-nav-child-indent',
                'text-nav-child transition-colors duration-hover',
                'hover:bg-raised hover:text-primary',
              )}
              activeProps={{ className: 'bg-accent-tint text-primary' }}
              inactiveProps={{ className: 'text-secondary' }}
            >
              <span className="truncate">{child.label}</span>
            </Link>
          </li>
        ))}
      </ul>
    </li>
  );
}
