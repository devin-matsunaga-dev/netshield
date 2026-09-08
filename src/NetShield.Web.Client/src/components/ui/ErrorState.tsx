import { Button } from '@/components/ui/Button';

interface ErrorStateProps {
  /** What failed, named. Not "something went wrong". */
  readonly title: string;
  /** What the reader can do about it. */
  readonly action: string;
  readonly onRetry?: () => void;
}

/**
 * Something failed, said in a way a reader can act on (DESIGN.md §8, CONVENTIONS.md §6).
 *
 * An error state says what failed and offers a retry. It names the thing that did not load
 * rather than the exception that was thrown — a caller who is handed a parser message learns
 * nothing they can use, and SPEC.md §5 does not let one leave the API in the first place.
 */
export function ErrorState({ title, action, onRetry }: ErrorStateProps) {
  return (
    <div role="alert" className="flex flex-col items-center gap-3 px-content py-12 text-center">
      <p className="text-body text-primary">{title}</p>
      <p className="text-metric-caption text-muted">{action}</p>
      {onRetry !== undefined && (
        <Button variant="secondary" onClick={onRetry}>
          Try again
        </Button>
      )}
    </div>
  );
}
