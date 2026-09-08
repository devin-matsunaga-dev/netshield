import { infiniteQueryOptions, queryOptions } from '@tanstack/react-query';

import { api } from '@/api/client';
import type { Schemas } from '@/api/types';
import type { CredentialListFilters } from '@/features/credentials/api/credentialFilters';
import { credentialKeys } from '@/features/credentials/api/credentialKeys';
import { ApiError } from '@/features/session/api/currentUserQuery';

export type CredentialProfileSummary = Schemas['CredentialProfileSummary'];
export type CredentialProfileDetail = Schemas['CredentialProfileDetail'];

/** How many rows a page carries. The API's own maximum is 200 (CONVENTIONS.md §4). */
const pageSize = 100;

/**
 * The credential profile list.
 *
 * Every route behind this feature is gated on `CredentialsManage`, which only an Administrator
 * holds — WP-1.2 settled that even a profile's *name* is behind it, because the list of names
 * says which accounts NetShield holds passwords for and a username is half an SSH credential.
 * Nothing in the response is a secret, and `ApiSecretExposureTests` fails the build if a shape
 * ever acquires one.
 */
export function credentialListQuery(filters: CredentialListFilters) {
  return infiniteQueryOptions({
    queryKey: credentialKeys.list(filters),
    queryFn: async ({ pageParam, signal }) => {
      const { data, response } = await api.GET('/api/v1/credential-profiles', {
        params: {
          query: {
            limit: pageSize,
            ...(pageParam === undefined ? {} : { cursor: pageParam }),
            ...(filters.kind === undefined ? {} : { kind: filters.kind }),
            ...(filters.search === undefined ? {} : { search: filters.search }),
          },
        },
        signal,
      });

      if (!response.ok || data === undefined) {
        throw new ApiError('Could not load the credential profiles.', response.status);
      }

      return data;
    },
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (last) => last.nextCursor ?? undefined,
  });
}

/**
 * One profile.
 *
 * It carries a name, a kind, a username and when its material last changed — and no material.
 * That is why the edit form can be prefilled and the rotation form cannot: there is nothing to
 * prefill it with, by design rather than by omission.
 */
export function credentialProfileQuery(id: string) {
  return queryOptions({
    queryKey: credentialKeys.detail(id),
    queryFn: async ({ signal }): Promise<CredentialProfileDetail> => {
      const { data, response } = await api.GET('/api/v1/credential-profiles/{id}', {
        params: { path: { id } },
        signal,
      });

      if (!response.ok || data === undefined) {
        throw new ApiError('Could not load the credential profile.', response.status);
      }

      return data;
    },
  });
}
