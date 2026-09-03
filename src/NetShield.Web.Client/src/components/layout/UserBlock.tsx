import { ChevronDown, LogOut } from 'lucide-react';
import { useEffect, useId, useRef, useState } from 'react';

import { useLogout } from '@/features/auth/api/logoutMutation';
import { useSession } from '@/features/session/hooks/useSession';
import { cn } from '@/lib/cn';

/**
 * The header's user block (DESIGN.md §5): avatar, name, role, chevron — and, behind the chevron,
 * the way out (WP-0.7).
 *
 * It renders below the `_app` guard, so there is a session to describe for as long as the shell
 * is up. There is no "signed out" state to draw here: a session that ends takes the whole shell
 * with it, and this draws nothing in the frame between the two.
 */
export function UserBlock() {
  const user = useSession();
  const logout = useLogout();
  const [open, setOpen] = useState(false);
  const menuId = useId();
  const container = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) {
      return undefined;
    }

    function dismiss(event: MouseEvent | KeyboardEvent) {
      if (event instanceof KeyboardEvent && event.key !== 'Escape') {
        return;
      }

      if (
        event instanceof MouseEvent &&
        container.current?.contains(event.target as Node) === true
      ) {
        return;
      }

      setOpen(false);
    }

    document.addEventListener('mousedown', dismiss);
    document.addEventListener('keydown', dismiss);

    return () => {
      document.removeEventListener('mousedown', dismiss);
      document.removeEventListener('keydown', dismiss);
    };
  }, [open]);

  // Only while a sign-out is tearing the session down. There is nothing truthful to put in the
  // header at that point, and the redirect to sign-in is already under way.
  if (user === undefined) {
    return null;
  }

  return (
    <div ref={container} className="relative">
      <button
        type="button"
        aria-haspopup="menu"
        aria-expanded={open}
        aria-controls={menuId}
        aria-label={`Account menu for ${user.displayName}`}
        onClick={() => {
          setOpen((wasOpen) => !wasOpen);
        }}
        className={cn(
          'flex h-control items-center gap-2 rounded-control px-2 text-left',
          'transition-colors duration-hover hover:bg-raised',
          'focus-visible:ring-2 focus-visible:ring-accent focus-visible:outline-none',
        )}
      >
        <span
          aria-hidden
          className="flex size-8 shrink-0 items-center justify-center rounded-full bg-accent-tint text-metric-label text-accent"
        >
          {initialsOf(user.displayName)}
        </span>
        <span className="hidden min-w-0 sm:block">
          <span className="block truncate text-metric-label text-primary">{user.displayName}</span>
          <span className="block truncate text-brand-caption text-muted">
            {roleLabel(user.role)}
          </span>
        </span>
        <ChevronDown
          aria-hidden
          className={cn(
            'size-4 shrink-0 text-muted transition-transform duration-hover',
            open && 'rotate-180',
          )}
        />
      </button>

      <div
        id={menuId}
        role="menu"
        hidden={!open}
        className="absolute right-0 z-40 mt-1 w-56 rounded-card border border-subtle bg-surface p-1"
      >
        <p className="px-3 py-2">
          <span className="block truncate text-metric-label text-primary">{user.username}</span>
          <span className="block truncate text-brand-caption text-muted">
            {roleLabel(user.role)}
          </span>
        </p>
        <div className="my-1 border-t border-subtle" />
        <button
          type="button"
          role="menuitem"
          disabled={logout.isPending}
          onClick={() => {
            logout.mutate();
          }}
          className={cn(
            'flex h-nav-item w-full items-center gap-3 rounded-control px-3',
            'text-nav-item text-secondary transition-colors duration-hover',
            'hover:bg-raised hover:text-primary',
            'focus-visible:ring-2 focus-visible:ring-accent focus-visible:outline-none',
            'disabled:cursor-not-allowed disabled:opacity-50',
          )}
        >
          <LogOut aria-hidden className="size-4 shrink-0" />
          <span>{logout.isPending ? 'Signing out' : 'Sign out'}</span>
        </button>
      </div>
    </div>
  );
}

function initialsOf(name: string): string {
  const parts = name.trim().split(/\s+/).filter(Boolean);
  const first = parts[0]?.[0] ?? '?';
  const second = parts.length > 1 ? (parts[parts.length - 1]?.[0] ?? '') : '';

  return (first + second).toUpperCase();
}

/** Sentence case everywhere (DESIGN.md §8). `ReadOnly` on the wire reads as "Read-only" here. */
function roleLabel(role: string): string {
  return role === 'ReadOnly' ? 'Read-only' : role;
}
