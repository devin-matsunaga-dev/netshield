import { PanelLeftClose, PanelLeftOpen } from 'lucide-react';

import { useVisibleNavigation } from '@/app/useVisibleNavigation';
import { Brand } from '@/components/layout/Brand';
import { SidebarNavLink } from '@/components/layout/SidebarNavLink';
import { SidebarSection } from '@/components/layout/SidebarSection';
import { cn } from '@/lib/cn';

interface SidebarProps {
  readonly collapsed: boolean;
  /** A column beside the content, rather than an overlay drawer over it (DESIGN.md §5). */
  readonly docked: boolean;
  readonly open: boolean;
  readonly onToggleCollapsed: () => void;
  readonly onNavigate: () => void;
}

/**
 * The sidebar (DESIGN.md §5): 200px expanded, 64px collapsed, full height, its own surface, a
 * hairline right border, and the collapse control pinned at the bottom above a top border.
 * Below 1024px it slides in over the content instead, where collapsing means nothing.
 *
 * It lists the destinations this session holds a permission for, and no others (WP-0.7).
 */
export function Sidebar({ collapsed, docked, open, onToggleCollapsed, onNavigate }: SidebarProps) {
  const CollapseIcon = collapsed ? PanelLeftOpen : PanelLeftClose;
  // Only what this session may reach. An entry it does not hold is absent rather than disabled.
  const navigation = useVisibleNavigation();

  return (
    <aside
      aria-hidden={!open}
      className={cn(
        'flex h-dvh shrink-0 flex-col border-r border-subtle bg-sidebar',
        'transition-[width,transform] duration-panel',
        collapsed ? 'w-sidebar-collapsed' : 'w-sidebar',
        docked ? 'static' : 'fixed inset-y-0 left-0 z-30',
        docked || open ? 'translate-x-0' : '-translate-x-full',
      )}
    >
      <Brand collapsed={collapsed} />

      <nav aria-label="Main" className="min-h-0 flex-1 overflow-y-auto px-2 py-3">
        <ul className="space-y-1">
          {navigation.map((entry) =>
            entry.children ? (
              <SidebarSection
                key={entry.label}
                entry={entry}
                collapsed={collapsed}
                onExpandSidebar={() => {
                  if (collapsed) {
                    onToggleCollapsed();
                  }
                }}
                onNavigate={onNavigate}
              />
            ) : entry.to ? (
              <SidebarNavLink
                key={entry.label}
                label={entry.label}
                to={entry.to}
                icon={entry.icon}
                collapsed={collapsed}
                onNavigate={onNavigate}
              />
            ) : null,
          )}
        </ul>
      </nav>

      {docked && (
        <div className="shrink-0 border-t border-subtle p-2">
          <button
            type="button"
            onClick={onToggleCollapsed}
            aria-label={collapsed ? 'Expand sidebar' : 'Collapse sidebar'}
            className={cn(
              'flex h-nav-item w-full items-center gap-3 rounded-control px-3',
              'text-nav-item text-secondary transition-colors duration-hover',
              'hover:bg-raised hover:text-primary',
              collapsed && 'justify-center px-0',
            )}
          >
            <CollapseIcon aria-hidden className="size-5 shrink-0" />
            {!collapsed && <span>Collapse</span>}
          </button>
        </div>
      )}
    </aside>
  );
}
