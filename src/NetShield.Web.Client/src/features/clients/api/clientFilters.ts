/** Everything the client list can be narrowed by. Every member is also a URL search parameter. */
export interface ClientListFilters {
  /** A MAC in any spelling, an IP address, or the start of a hostname. */
  readonly search?: string | undefined;
  /** Only clients a given device's port reports now. */
  readonly deviceId?: string | undefined;
  readonly vlanId?: number | undefined;
  /** Only clients an observation has confirmed within this window. */
  readonly seen?: SeenWindow | undefined;
  /** Only clients that hold an address now. */
  readonly onlyActive?: boolean | undefined;
}

/**
 * The recency windows the screen offers, as names rather than instants.
 *
 * A timestamp in the URL would go stale the moment somebody pasted the link — "since 11:04" means
 * something different tomorrow — while a window keeps meaning what the reader chose. The instant
 * is computed where the query is built.
 */
export const seenWindows = ['1h', '24h', '7d', '30d'] as const;

export type SeenWindow = (typeof seenWindows)[number];

const windowHours: Record<SeenWindow, number> = {
  '1h': 1,
  '24h': 24,
  '7d': 24 * 7,
  '30d': 24 * 30,
};

/** The window as an instant, resolved against now. */
export function seenSince(window: SeenWindow, now: Date = new Date()): string {
  return new Date(now.getTime() - windowHours[window] * 3_600_000).toISOString();
}

/**
 * Reads the filters out of a URL's search parameters.
 *
 * Anything unrecognised is dropped rather than passed on. A hand-edited address should leave the
 * reader on an unfiltered list, not on a 400 from the API — and a value the client did not
 * recognise is one the server would refuse anyway.
 *
 * This runs twice, deliberately: once in the route's `validateSearch` and once where the value is
 * used. A TanStack Router route inherits its parent's search parameters and merges its own over
 * them, so a value this route rejected survives as the root parsed it. WP-0.7 found the trap on
 * the sign-in return path and WP-1.7 found it again on the device filters; it is now three
 * occurrences of one problem, which is the point at which a shared "validated search" helper is
 * worth writing rather than each route remembering.
 */
export function parseClientFilters(search: Record<string, unknown>): ClientListFilters {
  return {
    ...text(search, 'search'),
    ...uuid(search, 'deviceId'),
    ...vlan(search),
    ...pick(search, 'seen', seenWindows),
    ...(search['onlyActive'] === true || search['onlyActive'] === 'true'
      ? { onlyActive: true }
      : {}),
  };
}

/** Whether anything is narrowing the list, which is what decides the empty state's wording. */
export function hasActiveFilter(filters: ClientListFilters): boolean {
  return (
    filters.search !== undefined ||
    filters.deviceId !== undefined ||
    filters.vlanId !== undefined ||
    filters.seen !== undefined ||
    filters.onlyActive === true
  );
}

const uuidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

function pick<T extends string>(
  search: Record<string, unknown>,
  name: string,
  permitted: readonly T[],
): Record<string, T> {
  const value = search[name];

  return typeof value === 'string' && (permitted as readonly string[]).includes(value)
    ? { [name]: value as T }
    : {};
}

function text(search: Record<string, unknown>, name: string): Record<string, string> {
  const value = search[name];

  return typeof value === 'string' && value.trim().length > 0 ? { [name]: value.trim() } : {};
}

function uuid(search: Record<string, unknown>, name: string): Record<string, string> {
  const value = search[name];

  return typeof value === 'string' && uuidPattern.test(value) ? { [name]: value } : {};
}

/**
 * A VLAN id, which arrives from the URL as text and has to be one of the 4,094 a tag can carry.
 * Anything else — a float, a negative, a word — is dropped rather than sent for the API to refuse.
 */
function vlan(search: Record<string, unknown>): Record<string, number> {
  const value = search['vlanId'];
  const parsed = typeof value === 'number' ? value : Number(value);

  return typeof value !== 'object' &&
    value !== undefined &&
    value !== '' &&
    Number.isInteger(parsed) &&
    parsed >= 1 &&
    parsed <= 4094
    ? { vlanId: parsed }
    : {};
}
