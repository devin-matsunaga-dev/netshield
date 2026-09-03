/** Where sign-in sends someone who arrived by choice rather than by being turned away. */
export const defaultReturnTo = '/overview';

/**
 * The return path, if it is one this application may navigate to, and the overview otherwise.
 *
 * The value comes off the URL, so it is whatever the person who wrote the link decided. Anything
 * that is not a plain in-application path is discarded: an absolute URL, a protocol-relative one
 * (`//elsewhere`, and `/\elsewhere`, which several browsers treat the same way) would turn the
 * sign-in page into an open redirect — a link that looks like NetShield's and lands somewhere
 * else, having just watched someone type a password.
 *
 * Checked here, at the point of use, rather than only in the route's `validateSearch`: a route
 * inherits its parent's search parameters and merges its own over them, so a value the child
 * rejected is still present as the parent saw it.
 */
export function safeReturnPath(value: unknown): string {
  if (typeof value !== 'string' || !value.startsWith('/')) {
    return defaultReturnTo;
  }

  const second = value[1];

  return second === '/' || second === '\\' ? defaultReturnTo : value;
}
