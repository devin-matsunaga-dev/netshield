import type { components } from '@/api/schema';

/** The response and request shapes the API describes, named for use across the SPA. */
export type Schemas = components['schemas'];

/** Who the current session belongs to. */
export type AuthenticatedUser = Schemas['AuthenticatedUser'];

/** The role a session carries. */
export type UserRole = Schemas['UserRole'];

/**
 * What a session may do.
 *
 * Resolved on the server from the role and sent for presentation only: it decides which nav
 * entries and write controls the SPA draws, and nothing else. Every protected request is checked
 * against the same table again on the server (ARCHITECTURE.md §8), so hiding a control is a
 * courtesy rather than a boundary.
 */
export type Permission = Schemas['Permission'];

/** The RFC 9457 body every API error carries (CONVENTIONS.md §4). */
export type ProblemDetails = Schemas['ProblemDetails'];
