import { useState, type ReactNode } from 'react';

import { Header } from '@/components/layout/Header';
import { Sidebar } from '@/components/layout/Sidebar';
import { useMediaQuery } from '@/lib/useMediaQuery';
import { useSidebarCollapsed } from '@/lib/useSidebarCollapsed';

interface AppShellProps {
  readonly children: ReactNode;
}

/** Below this the sidebar is an overlay drawer rather than a column (DESIGN.md §5). */
const docked = '(min-width: 1024px)';

/**
 * The chrome every route renders inside (DESIGN.md §5): sidebar, header, and a content area on
 * the page background with 24px padding and a 1600px cap.
 */
export function AppShell({ children }: AppShellProps) {
  const [collapsed, toggleCollapsed] = useSidebarCollapsed();
  const isDocked = useMediaQuery(docked);
  const [drawerOpen, setDrawerOpen] = useState(false);

  // Collapsing is a decision about a column. A drawer is either open or it is not.
  const isCollapsed = isDocked && collapsed;

  return (
    <div className="flex h-dvh overflow-hidden bg-base">
      <Sidebar
        collapsed={isCollapsed}
        docked={isDocked}
        open={isDocked || drawerOpen}
        onToggleCollapsed={toggleCollapsed}
        onNavigate={() => {
          setDrawerOpen(false);
        }}
      />

      {!isDocked && drawerOpen && (
        <button
          type="button"
          aria-label="Close navigation"
          onClick={() => {
            setDrawerOpen(false);
          }}
          className="fixed inset-0 z-20 bg-base/70"
        />
      )}

      <div className="flex min-w-0 flex-1 flex-col">
        <Header
          {...(isDocked
            ? {}
            : {
                onOpenNavigation: () => {
                  setDrawerOpen(true);
                },
              })}
        />
        <main className="min-h-0 flex-1 overflow-y-auto p-content">
          <div className="mx-auto max-w-content">{children}</div>
        </main>
      </div>
    </div>
  );
}
