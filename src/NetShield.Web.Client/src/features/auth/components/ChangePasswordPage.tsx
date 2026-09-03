import { useNavigate } from '@tanstack/react-router';
import { useState, type SyntheticEvent } from 'react';

import { Button } from '@/components/ui/Button';
import { FormMessage } from '@/components/ui/FormMessage';
import { TextField } from '@/components/ui/TextField';
import {
  PasswordChangeError,
  useChangePassword,
  type PasswordField,
} from '@/features/auth/api/changePasswordMutation';
import { AuthLayout } from '@/features/auth/components/AuthLayout';
import { useLogout } from '@/features/auth/api/logoutMutation';

/** The one rule the client can check on its own: the two new entries have to agree. */
const mismatch = 'The two new passwords do not match.';

/**
 * The forced password change (WP-0.7).
 *
 * A seeded first-run administrator, and anyone whose password an administrator resets, arrives
 * here and can reach nothing else: WP-0.5 refuses every other authenticated route while the flag
 * stands. Without this screen such a session meets a 403 everywhere and no explanation.
 *
 * Every rule but the confirmation match is the server's. The policy is configurable and lives
 * there; restating it here would be a second copy that is wrong on the first installation that
 * changes one.
 */
export function ChangePasswordPage() {
  const navigate = useNavigate();
  const change = useChangePassword();
  const logout = useLogout();
  const [currentPassword, setCurrentPassword] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [confirmation, setConfirmation] = useState('');
  const [confirmationError, setConfirmationError] = useState<string>();

  const rejection = change.error instanceof PasswordChangeError ? change.error : undefined;
  const errorFor = (field: PasswordField): string | undefined =>
    rejection?.field === field ? rejection.message : undefined;

  async function submit(event: SyntheticEvent) {
    event.preventDefault();
    setConfirmationError(undefined);

    if (newPassword !== confirmation) {
      setConfirmationError(mismatch);
      return;
    }

    const user = await change.mutateAsync({ currentPassword, newPassword }).catch(() => null);

    if (user !== null) {
      await navigate({ to: '/overview', replace: true });
    }
  }

  return (
    <AuthLayout
      title="Change your password"
      subtitle="This account has to choose a new password before it can be used."
    >
      <form onSubmit={(event) => void submit(event)} className="space-y-4" noValidate>
        {/* Only the failures that belong to no single field. The rest sit beside their input. */}
        <FormMessage>{rejection?.field === null ? rejection.message : undefined}</FormMessage>

        <TextField
          label="Current password"
          name="currentPassword"
          type="password"
          value={currentPassword}
          onChange={(event) => {
            setCurrentPassword(event.target.value);
          }}
          error={errorFor('currentPassword')}
          autoComplete="current-password"
          autoFocus
          required
          disabled={change.isPending}
        />

        <TextField
          label="New password"
          name="newPassword"
          type="password"
          value={newPassword}
          onChange={(event) => {
            setNewPassword(event.target.value);
          }}
          error={errorFor('newPassword')}
          autoComplete="new-password"
          required
          disabled={change.isPending}
        />

        <TextField
          label="Confirm new password"
          name="confirmation"
          type="password"
          value={confirmation}
          onChange={(event) => {
            setConfirmation(event.target.value);
          }}
          error={confirmationError}
          autoComplete="new-password"
          required
          disabled={change.isPending}
        />

        <Button type="submit" fullWidth disabled={change.isPending}>
          {change.isPending ? 'Changing password' : 'Change password'}
        </Button>

        {/* The only other way out. Everything else this session can reach is refused. */}
        <Button
          variant="ghost"
          fullWidth
          onClick={() => {
            logout.mutate();
          }}
          disabled={logout.isPending}
        >
          Sign out
        </Button>
      </form>
    </AuthLayout>
  );
}
