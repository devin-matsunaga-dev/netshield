import { Bell } from 'lucide-react';

interface NotificationBellProps {
  /** Unread notifications. Nothing counts them until the alerting work in Phase 6. */
  readonly count?: number;
}

/** The header's notification bell with its count badge (DESIGN.md §5). */
export function NotificationBell({ count = 0 }: NotificationBellProps) {
  const label = count > 0 ? `Notifications, ${String(count)} unread` : 'Notifications';

  return (
    <button
      type="button"
      aria-label={label}
      className="relative flex size-9 items-center justify-center rounded-control text-secondary transition-colors duration-hover hover:bg-raised hover:text-primary"
    >
      <Bell aria-hidden className="size-5" />
      {count > 0 && (
        <span className="tabular absolute -top-0.5 -right-0.5 min-w-4 rounded-full bg-danger px-1 text-badge text-primary">
          {count}
        </span>
      )}
    </button>
  );
}
