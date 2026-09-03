import { CircleQuestionMark, Menu } from 'lucide-react';

import { NotificationBell } from '@/components/layout/NotificationBell';
import { SearchField } from '@/components/layout/SearchField';
import { ThemeToggle } from '@/components/layout/ThemeToggle';
import { UserBlock } from '@/components/layout/UserBlock';

interface HeaderProps {
  /** Present only when the sidebar is a drawer, which is what opens it (DESIGN.md §5). */
  readonly onOpenNavigation?: () => void;
}

/**
 * The header (DESIGN.md §5): 64px, the sidebar's surface, a hairline bottom border. Search on the
 * left; bell, help, theme toggle and the user block right-aligned.
 */
export function Header({ onOpenNavigation }: HeaderProps) {
  return (
    <header className="flex h-header shrink-0 items-center gap-4 border-b border-subtle bg-sidebar px-content">
      {onOpenNavigation !== undefined && (
        <button
          type="button"
          onClick={onOpenNavigation}
          aria-label="Open navigation"
          className="flex size-9 shrink-0 items-center justify-center rounded-control text-secondary transition-colors duration-hover hover:bg-raised hover:text-primary"
        >
          <Menu aria-hidden className="size-5" />
        </button>
      )}

      <SearchField />

      <div className="ml-auto flex items-center gap-1">
        <NotificationBell />
        <button
          type="button"
          aria-label="Help"
          className="flex size-9 items-center justify-center rounded-control text-secondary transition-colors duration-hover hover:bg-raised hover:text-primary"
        >
          <CircleQuestionMark aria-hidden className="size-5" />
        </button>
        <ThemeToggle />
        <UserBlock />
      </div>
    </header>
  );
}
