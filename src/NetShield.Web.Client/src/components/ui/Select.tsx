import { useId, type SelectHTMLAttributes } from 'react';

import { cn } from '@/lib/cn';

/** One choice. `value` is what travels in the URL; `label` is what the reader sees. */
export interface SelectOption {
  readonly value: string;
  readonly label: string;
}

interface SelectProps extends Omit<SelectHTMLAttributes<HTMLSelectElement>, 'className' | 'id'> {
  readonly label: string;
  readonly options: readonly SelectOption[];
  /** The label for "no filter". Absent means the field is a required choice. */
  readonly anyLabel?: string;
}

/**
 * A labelled select (DESIGN.md §3, §6): the raised surface, a strong border, 8px radius, the
 * same 36px control height as every button and text field beside it.
 *
 * A native `<select>` rather than a listbox built out of divs. It is keyboard reachable and
 * screen-reader correct without a line of code, which is what CONVENTIONS.md §6 asks for, and
 * nothing in the reference screenshot needs more than one.
 */
export function Select({ label, options, anyLabel, ...props }: SelectProps) {
  const id = useId();

  return (
    <div className="space-y-1.5">
      <label htmlFor={id} className="block text-metric-label text-secondary">
        {label}
      </label>
      <select
        id={id}
        className={cn(
          'h-control w-full rounded-control border border-strong bg-raised px-3',
          'text-body text-primary',
          'transition-colors duration-hover',
          'focus-visible:ring-2 focus-visible:ring-accent focus-visible:outline-none',
          'disabled:cursor-not-allowed disabled:opacity-50',
        )}
        {...props}
      >
        {anyLabel !== undefined && <option value="">{anyLabel}</option>}
        {options.map((option) => (
          <option key={option.value} value={option.value}>
            {option.label}
          </option>
        ))}
      </select>
    </div>
  );
}
