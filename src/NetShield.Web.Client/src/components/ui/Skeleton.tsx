import { cn } from '@/lib/cn';

interface SkeletonProps {
  /** A Tailwind width utility. The skeleton has to match the shape it stands in for. */
  readonly className?: string;
}

/**
 * A loading placeholder (CONVENTIONS.md §6): a skeleton matching the final layout, never a
 * centred spinner on a full page.
 *
 * The pulse is a 1.5s opacity animation and nothing more — DESIGN.md §7 admits no shimmer beyond
 * that, and it is inside `motion-safe` so a reader who asked for less motion gets a still block
 * rather than none at all.
 */
export function Skeleton({ className }: SkeletonProps) {
  return (
    <div
      aria-hidden="true"
      className={cn('h-4 rounded-control bg-raised motion-safe:animate-pulse', className)}
    />
  );
}
