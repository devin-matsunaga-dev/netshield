import { useMutation, useQueryClient, type UseMutationResult } from '@tanstack/react-query';

import { api } from '@/api/client';
import type { AuthenticatedUser } from '@/api/types';
import { sessionKeys } from '@/features/session/api/sessionKeys';

/** What the change-password form sends. */
export interface PasswordChange {
  readonly currentPassword: string;
  readonly newPassword: string;
}

/** Which field a rejection belongs beside, or neither. */
export type PasswordField = 'currentPassword' | 'newPassword' | null;

/** A rejected change, split so the form can put each part beside the field it belongs to. */
export class PasswordChangeError extends Error {
  public constructor(
    message: string,
    public readonly field: PasswordField,
  ) {
    super(message);
    this.name = 'PasswordChangeError';
  }
}

const unavailable = 'Could not reach NetShield. Check your connection and try again.';

/**
 * Changes the caller's own password.
 *
 * A wrong *current* password is a 422 rather than a 401 — WP-0.4 chose that deliberately, so
 * that a typo here does not trip the "401 anywhere signs you out" rule and throw away a session
 * that was perfectly valid. Every rejection is told apart by the problem's `code` rather than by
 * its status, because the policy rejection and the wrong-current-password rejection share one.
 *
 * The API replies with a fresh session and revokes every other one the account holds, which is
 * what someone changing a password after suspecting it is known would want.
 */
export function useChangePassword(): UseMutationResult<AuthenticatedUser, Error, PasswordChange> {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (change: PasswordChange) => {
      const { data, error, response } = await api.POST('/api/v1/auth/password', { body: change });

      if (response.ok && data !== undefined) {
        return data;
      }

      throw rejectionOf(error, response.status);
    },
    onSuccess: (user) => {
      // The response carries the new session, `mustChangePassword` already cleared, so the guard
      // that sent the user here lets them past on the very next navigation.
      queryClient.setQueryData(sessionKeys.current(), user);
    },
  });
}

/**
 * The problem details a failed change carried, read as a message and the field it belongs to.
 *
 * The wording is the server's own wherever the server had some. The password policy is
 * configurable and lives entirely on the server; restating its rules here would be a second copy
 * to keep in step, and it would be wrong on the first installation that changed one.
 */
function rejectionOf(problem: unknown, status: number): PasswordChangeError {
  const details = problem as
    { code?: string; detail?: string; errors?: Record<string, string[]> } | undefined;

  switch (details?.code) {
    case 'identity.current-password-invalid':
      return new PasswordChangeError('That is not your current password.', 'currentPassword');

    case 'identity.password-unchanged':
      return new PasswordChangeError(
        'The new password must be different from your current one.',
        'newPassword',
      );

    case 'identity.password-policy':
      return new PasswordChangeError(
        firstFailure(details.errors) ?? 'That password does not meet the password policy.',
        'newPassword',
      );

    case 'request.invalid':
      return new PasswordChangeError(firstFailure(details.errors) ?? 'Fill in every field.', null);

    default:
      return new PasswordChangeError(
        status === 401 ? 'Your session has ended. Sign in again.' : unavailable,
        null,
      );
  }
}

function firstFailure(errors: Record<string, string[]> | undefined): string | undefined {
  return Object.values(errors ?? {}).flat()[0];
}
