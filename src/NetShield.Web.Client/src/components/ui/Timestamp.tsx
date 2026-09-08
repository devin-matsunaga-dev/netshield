import { formatTimestamp, formatTimestampTitle } from '@/lib/timestamp';

interface TimestampProps {
  readonly value: string | null | undefined;
}

/**
 * A time, written the way DESIGN.md §8 asks: relative under 24 hours, absolute beyond, and the
 * exact value with its timezone on hover.
 *
 * A `<time>` element with a machine-readable `dateTime`, so the value is still the value to
 * anything reading the page that is not a pair of eyes.
 */
export function Timestamp({ value }: TimestampProps) {
  if (value === null || value === undefined) {
    return <span className="text-muted">—</span>;
  }

  return (
    <time dateTime={value} title={formatTimestampTitle(value)}>
      {formatTimestamp(value)}
    </time>
  );
}
