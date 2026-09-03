import { useId, type InputHTMLAttributes } from 'react';

import { cn } from '@/lib/cn';

interface TextFieldProps extends Omit<InputHTMLAttributes<HTMLInputElement>, 'className' | 'id'> {
  readonly label: string;
  /** What went wrong with this field. Its presence is what marks the input invalid. */
  readonly error?: string | undefined;
  /** A hint under the field — the password policy, for instance. */
  readonly hint?: string | undefined;
}

/**
 * A labelled text input (DESIGN.md §3, §6): the raised surface, a strong border, 8px radius.
 *
 * The label is a real `<label>` bound by id rather than a placeholder, so the field still says
 * what it is once there is text in it. An error is announced through `aria-describedby` and
 * `aria-invalid`, and carries an icon-free wording that says what to do (DESIGN.md §8).
 */
export function TextField({ label, error, hint, ...props }: TextFieldProps) {
  const id = useId();
  const errorId = `${id}-error`;
  const hintId = `${id}-hint`;
  const described = [hint === undefined ? null : hintId, error === undefined ? null : errorId]
    .filter((value) => value !== null)
    .join(' ');

  return (
    <div className="space-y-1.5">
      <label htmlFor={id} className="block text-metric-label text-secondary">
        {label}
      </label>
      <input
        id={id}
        aria-invalid={error !== undefined}
        aria-describedby={described.length > 0 ? described : undefined}
        className={cn(
          'h-control w-full rounded-control border bg-raised px-3',
          'text-body text-primary placeholder:text-muted',
          'transition-colors duration-hover',
          'focus-visible:ring-2 focus-visible:ring-accent focus-visible:outline-none',
          'disabled:cursor-not-allowed disabled:opacity-50',
          error === undefined ? 'border-strong' : 'border-danger',
        )}
        {...props}
      />
      {hint !== undefined && (
        <p id={hintId} className="text-metric-caption text-muted">
          {hint}
        </p>
      )}
      {error !== undefined && (
        <p id={errorId} className="text-metric-caption text-danger">
          {error}
        </p>
      )}
    </div>
  );
}
