import type { components } from '@/api/schema';

/** The response and request shapes the API describes, named for use across the SPA. */
export type Schemas = components['schemas'];

/** Who the current session belongs to. */
export type AuthenticatedUser = Schemas['AuthenticatedUser'];

/** The role a session carries. Permissions are resolved on the server and never sent. */
export type UserRole = Schemas['UserRole'];

/** The RFC 9457 body every API error carries (CONVENTIONS.md §4). */
export type ProblemDetails = Schemas['ProblemDetails'];
