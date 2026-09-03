import { useQuery } from '@tanstack/react-query';
import { ChevronDown } from 'lucide-react';

import { currentUserQuery } from '@/features/session/api/currentUserQuery';

/**
 * The header's user block (DESIGN.md §5): avatar, name, role, chevron.
 *
 * It reads the session from the API through the generated client, which is the whole of what
 * WP-0.6 does with a session. There is no menu behind the chevron, no guard, and no redirect
 * when the read fails — those are WP-0.7.
 */
export function UserBlock() {
  const { data: user, isPending } = useQuery(currentUserQuery());

  const name = user?.displayName ?? (isPending ? 'Loading' : 'No session');
  const role = user?.role ?? '—';

  return (
    <button
      type="button"
      aria-label={`Account menu for ${name}`}
      className="flex h-control items-center gap-2 rounded-control px-2 text-left transition-colors duration-hover hover:bg-raised"
    >
      <span
        aria-hidden
        className="flex size-8 shrink-0 items-center justify-center rounded-full bg-accent-tint text-metric-label text-accent"
      >
        {initialsOf(name)}
      </span>
      <span className="hidden min-w-0 sm:block">
        <span className="block truncate text-metric-label text-primary">{name}</span>
        <span className="block truncate text-brand-caption text-muted">{roleLabel(role)}</span>
      </span>
      <ChevronDown aria-hidden className="size-4 shrink-0 text-muted" />
    </button>
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
