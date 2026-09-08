import { createFileRoute } from '@tanstack/react-router';

import {
  parseCredentialFilters,
  type CredentialListFilters,
} from '@/features/credentials/api/credentialFilters';
import { CredentialListPage } from '@/features/credentials/components/CredentialListPage';

/**
 * The credential profiles screen, at `/devices/credentials` with no sidebar entry — the sidebar
 * is the reference screenshot's and DESIGN.md §9 admits no invented visual direction, which is
 * the same answer WP-1.7 gave for the discovery screen.
 *
 * The filters are parsed twice, deliberately, for the reason every other URL-state screen does
 * it: a TanStack Router route inherits its parent's search parameters and merges its own over
 * them, so a value this route rejected survives as the root parsed it.
 */
export const Route = createFileRoute('/_app/devices/credentials')({
  validateSearch: (search: Record<string, unknown>): CredentialListFilters =>
    parseCredentialFilters(search),
  component: function CredentialProfiles() {
    return <CredentialListPage filters={parseCredentialFilters(Route.useSearch())} />;
  },
});
