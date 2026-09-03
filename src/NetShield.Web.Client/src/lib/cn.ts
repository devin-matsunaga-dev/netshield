import { clsx, type ClassValue } from 'clsx';
import { twMerge } from 'tailwind-merge';

/**
 * Joins Tailwind class names, letting a later one win over an earlier one of the same kind.
 * The only place conditional styling is assembled — CONVENTIONS.md §6 admits no inline style
 * object except computed geometry.
 */
export function cn(...classes: ClassValue[]): string {
  return twMerge(clsx(classes));
}
