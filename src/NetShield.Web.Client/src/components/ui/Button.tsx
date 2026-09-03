import type { ButtonHTMLAttributes, ReactNode } from 'react';

import { cn } from '@/lib/cn';

/** The three button kinds DESIGN.md §6 names. Destructive arrives with the first thing to destroy. */
type ButtonVariant = 'primary' | 'secondary' | 'ghost';

interface ButtonProps extends Omit<ButtonHTMLAttributes<HTMLButtonElement>, 'className'> {
  readonly variant?: ButtonVariant;
  readonly fullWidth?: boolean;
  readonly children: ReactNode;
}

/**
 * A button (DESIGN.md §6): 36px tall, 8px radius, 14px/500, 16px of horizontal padding.
 *
 * Primary fills with the accent, secondary sits on the raised surface behind a hairline, ghost
 * is transparent until hovered. Every one of them carries a visible focus ring — CONVENTIONS.md
 * §6 requires one on every interactive element, and the default outline does not survive a dark
 * surface.
 */
const variants: Record<ButtonVariant, string> = {
  primary: 'bg-accent text-white hover:opacity-90',
  secondary: 'bg-raised text-primary border border-subtle hover:border-strong',
  ghost: 'text-secondary hover:bg-raised hover:text-primary',
};

export function Button({
  variant = 'primary',
  fullWidth = false,
  type = 'button',
  children,
  ...props
}: ButtonProps) {
  return (
    <button
      // Defaults to `button`: one inside a form would otherwise submit it by accident.
      type={type}
      className={cn(
        'inline-flex h-control items-center justify-center gap-2 rounded-control px-4',
        'text-nav-item transition-colors duration-hover',
        'focus-visible:ring-2 focus-visible:ring-accent focus-visible:outline-none',
        'disabled:cursor-not-allowed disabled:opacity-50',
        variants[variant],
        fullWidth && 'w-full',
      )}
      {...props}
    >
      {children}
    </button>
  );
}
