import { Link } from '@tanstack/react-router';
import type { LucideIcon } from 'lucide-react';

import { cn } from '@/lib/cn';

interface SidebarNavLinkProps {
  readonly label: string;
  readonly to: string;
  readonly icon: LucideIcon;
  readonly collapsed: boolean;
  /** Closes the drawer once a destination has been chosen from it (DESIGN.md §5). */
  readonly onNavigate: () => void;
}

/**
 * A destination in the sidebar (DESIGN.md §6, "Nav item"): 40px tall, 8px radius, a 20px icon
 * and a label. Active adds the accent tint and a 3px bar flush to the sidebar's left edge.
 */
export function SidebarNavLink({
  label,
  to,
  icon: Icon,
  collapsed,
  onNavigate,
}: SidebarNavLinkProps) {
  return (
    <li className="relative">
      <Link
        to={to}
        onClick={onNavigate}
        title={collapsed ? label : undefined}
        className={cn(
          'group flex h-nav-item items-center gap-3 rounded-control px-3',
          'text-nav-item transition-colors duration-hover',
          'hover:bg-raised hover:text-primary',
          collapsed && 'justify-center px-0',
        )}
        // Rest and active are set here rather than one overriding the other in the class list:
        // two utilities of the same kind have equal specificity, so which one won would depend
        // on the order Tailwind happened to emit them in.
        activeProps={{ className: 'bg-accent-tint text-primary' }}
        inactiveProps={{ className: 'text-secondary' }}
      >
        {({ isActive }) => (
          <>
            {isActive && (
              <span
                aria-hidden
                className="absolute top-1/2 -left-2 h-6 w-[3px] -translate-y-1/2 rounded-r bg-accent"
              />
            )}
            <Icon aria-hidden className="size-5 shrink-0" />
            {!collapsed && <span className="truncate">{label}</span>}
            {collapsed && <span className="sr-only">{label}</span>}
          </>
        )}
      </Link>
    </li>
  );
}
