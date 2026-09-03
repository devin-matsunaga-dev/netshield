import { CircleAlert } from 'lucide-react';

interface FormMessageProps {
  /** What failed and what to do about it (DESIGN.md §8). Absent means the form has not failed. */
  readonly children?: string | undefined;
}

/**
 * A form-level failure — the one that belongs to the whole submission rather than to a field.
 *
 * Colour is never the only signal (DESIGN.md §9.2), so the tinted panel carries an icon and the
 * text says what happened. `role="alert"` because the reader's attention is elsewhere: they
 * pressed a button and are waiting to be let in.
 */
export function FormMessage({ children }: FormMessageProps) {
  if (children === undefined) {
    return null;
  }

  return (
    <p
      role="alert"
      className="flex items-start gap-2 rounded-control bg-danger-tint px-3 py-2 text-body text-danger"
    >
      <CircleAlert aria-hidden className="mt-0.5 size-4 shrink-0" />
      <span>{children}</span>
    </p>
  );
}
