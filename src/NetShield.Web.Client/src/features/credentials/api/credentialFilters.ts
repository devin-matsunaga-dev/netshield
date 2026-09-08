import type { Schemas } from '@/api/types';

/** Everything the profile list can be narrowed by. Every member is also a URL search parameter. */
export interface CredentialListFilters {
  readonly kind?: Schemas['CredentialKind'] | undefined;
  readonly search?: string | undefined;
}

/** The kinds WP-1.2 fixed. A profile's kind is chosen at creation and never changes. */
export const credentialKinds = ['SnmpV2c', 'SnmpV3', 'SshPassword', 'SshKey'] as const;

/**
 * Reads the filters out of a URL's search parameters.
 *
 * Anything unrecognised is dropped rather than passed on, and this runs where the value is
 * *used* as well as in the route's `validateSearch` — a TanStack Router route inherits its
 * parent's search parameters and merges its own over them, so a value the route rejected
 * survives as the root parsed it. Fourth occurrence of the same trap (WP-0.7, WP-1.7, WP-1.8).
 */
export function parseCredentialFilters(search: Record<string, unknown>): CredentialListFilters {
  const kind = search['kind'];
  const term = search['search'];

  return {
    ...(typeof kind === 'string' && (credentialKinds as readonly string[]).includes(kind)
      ? { kind: kind as Schemas['CredentialKind'] }
      : {}),
    ...(typeof term === 'string' && term.trim().length > 0 ? { search: term.trim() } : {}),
  };
}

/** Whether anything is narrowing the list, which is what decides the empty state's wording. */
export function hasActiveFilter(filters: CredentialListFilters): boolean {
  return filters.kind !== undefined || filters.search !== undefined;
}
