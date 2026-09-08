/**
 * How NetShield writes a time (DESIGN.md §8): relative under 24 hours — "2 min ago" — and
 * absolute beyond, always with the reader's own timezone available on hover.
 *
 * In `src/lib` rather than in a feature, because devices, discovery runs and everything Phase 3
 * onwards adds all write a timestamp, and CONVENTIONS.md §6 keeps nothing shared inside a
 * feature folder.
 */

/** The relative-or-absolute label. `null` and `undefined` are an em dash, not "Invalid Date". */
export function formatTimestamp(value: string | null | undefined, now = new Date()): string {
  if (value === null || value === undefined) {
    return '—';
  }

  const at = new Date(value);

  if (Number.isNaN(at.getTime())) {
    return '—';
  }

  const seconds = Math.round((now.getTime() - at.getTime()) / 1000);

  // A clock that is a little behind the server's should not produce "in 3 seconds".
  if (seconds < 60) {
    return seconds < 5 ? 'just now' : `${Math.max(seconds, 0).toString()} sec ago`;
  }

  if (seconds < 3_600) {
    return `${Math.floor(seconds / 60).toString()} min ago`;
  }

  if (seconds < 86_400) {
    const hours = Math.floor(seconds / 3_600);

    return `${hours.toString()} ${hours === 1 ? 'hour' : 'hours'} ago`;
  }

  return at.toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' });
}

/**
 * The full value for the `title` attribute — the timezone DESIGN.md §8 asks to be shown on
 * hover. Resolved from the browser rather than configured, because nothing stores a per-user
 * timezone yet and inventing one would be inventing a setting.
 */
export function formatTimestampTitle(value: string | null | undefined): string | undefined {
  if (value === null || value === undefined) {
    return undefined;
  }

  const at = new Date(value);

  return Number.isNaN(at.getTime())
    ? undefined
    : at.toLocaleString(undefined, { timeZoneName: 'short' });
}
