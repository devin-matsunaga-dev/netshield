import { useMutation, useQueryClient } from '@tanstack/react-query';

import { api } from '@/api/client';
import type { Schemas } from '@/api/types';
import { credentialKeys } from '@/features/credentials/api/credentialKeys';

export type CreateCredentialProfileRequest = Schemas['CreateCredentialProfileRequest'];
export type UpdateCredentialProfileRequest = Schemas['UpdateCredentialProfileRequest'];
export type CredentialMaterial = Schemas['CredentialMaterial'];

/**
 * An API refusal a form can act on: the problem's `code` says which rule was broken and `errors`
 * says which field it was about.
 *
 * It matters more here than on the device form, because the rule that decides which members a
 * kind needs is a *semantic* one: WP-1.2 settled that "this kind requires a community string" is
 * a 422 from `CredentialKindRules` rather than a 400 from a validator, since it is a fact about
 * the profile's stored kind rather than about the request's shape. So a form that branched on
 * the status alone would put a kind mismatch and a malformed name in the same place.
 */
export class CredentialRequestError extends Error {
  public constructor(
    message: string,
    public readonly code: string | undefined,
    public readonly fieldErrors: Record<string, string[]>,
  ) {
    super(message);
    this.name = 'CredentialRequestError';
  }
}

export function useCreateCredentialProfile() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (request: CreateCredentialProfileRequest) => {
      const { data, error, response } = await api.POST('/api/v1/credential-profiles', {
        body: request,
      });

      if (!response.ok || data === undefined) {
        throw toRequestError(error, 'The credential profile could not be created.');
      }

      return data;
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: credentialKeys.lists() }),
  });
}

/**
 * Changes what a profile is called and who it authenticates as — and nothing about its secret.
 *
 * The request has no material member and could not have one: WP-1.2 settled that a value which
 * is never returned cannot be round-tripped through a whole-resource PUT, because a caller
 * reading the profile back would have nothing to send and the obvious reading of an absent value
 * would erase the credential. Replacing the secret is `useRotateCredentialMaterial` below.
 *
 * The kind is absent too, and fixed at creation: it decides what the sealed blob contains, so a
 * profile whose kind changed would hold material describing a protocol it no longer claims to be
 * for.
 */
export function useUpdateCredentialProfile(id: string) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (request: UpdateCredentialProfileRequest) => {
      const { data, error, response } = await api.PUT('/api/v1/credential-profiles/{id}', {
        params: { path: { id } },
        body: request,
      });

      if (!response.ok || data === undefined) {
        throw toRequestError(error, 'The credential profile could not be saved.');
      }

      return data;
    },
    onSuccess: async (profile) => {
      queryClient.setQueryData(credentialKeys.detail(id), profile);
      await queryClient.invalidateQueries({ queryKey: credentialKeys.lists() });
    },
  });
}

/**
 * Replaces a profile's secret.
 *
 * **Nothing about the value being sent is kept.** It is not put in a query key, it is not written
 * into the cache, and the mutation returns the profile rather than what was sent — so the only
 * place the plaintext ever exists on this side is the field the reader typed it into and the
 * request body, and the form clears the first as soon as the second succeeds.
 *
 * Rotating needs no knowledge of the old value, which is the point: an administrator replacing a
 * community string somebody else set should not have to know what it was.
 */
export function useRotateCredentialMaterial(id: string) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (material: CredentialMaterial) => {
      const { data, error, response } = await api.PUT('/api/v1/credential-profiles/{id}/material', {
        params: { path: { id } },
        body: { material },
      });

      if (!response.ok || data === undefined) {
        throw toRequestError(error, 'The credential could not be replaced.');
      }

      return data;
    },
    onSuccess: async (profile) => {
      queryClient.setQueryData(credentialKeys.detail(id), profile);
      await queryClient.invalidateQueries({ queryKey: credentialKeys.lists() });
    },
  });
}

/**
 * Removes a profile.
 *
 * A soft delete on the row, and a hard delete of every device assignment of it — so a device
 * that was reached with this credential stops being reachable at all until another is assigned,
 * which is why the confirmation says how many devices that is.
 */
export function useDeleteCredentialProfile() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (id: string) => {
      const { error, response } = await api.DELETE('/api/v1/credential-profiles/{id}', {
        params: { path: { id } },
      });

      if (!response.ok) {
        throw toRequestError(error, 'The credential profile could not be removed.');
      }
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: credentialKeys.all }),
  });
}

/**
 * Turns an RFC 9457 body into something a form can render.
 *
 * Read structurally rather than as the generated `ProblemDetails`, because the document declares
 * neither `code` nor `errors` and `code` is what every refusal is told apart by — the same
 * reading WP-0.7, WP-1.7 and WP-1.8 already use.
 */
function toRequestError(error: unknown, fallback: string): CredentialRequestError {
  const problem = error as
    { detail?: string; code?: string; errors?: Record<string, string[]> } | undefined;

  return new CredentialRequestError(
    problem?.detail ?? fallback,
    problem?.code,
    problem?.errors ?? {},
  );
}
