import { useNavigate, useSearch } from '@tanstack/react-router';
import { useState, type SyntheticEvent } from 'react';

import { Button } from '@/components/ui/Button';
import { FormMessage } from '@/components/ui/FormMessage';
import { TextField } from '@/components/ui/TextField';
import { useLogin } from '@/features/auth/api/loginMutation';
import { AuthLayout } from '@/features/auth/components/AuthLayout';
import { safeReturnPath } from '@/features/auth/returnPath';

/**
 * The sign-in screen (WP-0.7).
 *
 * One message for every refusal, because the API answers an unknown username, a wrong password,
 * a disabled account and a locked-out one identically — WP-0.4 chose that so a caller cannot
 * enumerate the user table, and saying more here would give away what the status code withholds.
 */
export function LoginPage() {
  // Where the guard turned the reader away from, sanitised — the value came off the URL, so it
  // is whatever the person who wrote the link decided.
  const returnTo = safeReturnPath(useSearch({ from: '/login' }).redirect);
  const navigate = useNavigate();
  const login = useLogin();
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');

  async function submit(event: SyntheticEvent) {
    event.preventDefault();

    const user = await login.mutateAsync({ username, password }).catch(() => null);

    if (user === null) {
      return;
    }

    // A first-run administrator owes a password change before anything else. The `_app` guard
    // would bounce them there anyway; going straight avoids a visible flash of the shell.
    await navigate({
      to: user.mustChangePassword ? '/change-password' : returnTo,
      replace: true,
    });
  }

  return (
    <AuthLayout title="Sign in" subtitle="Sign in to reach your network and security console.">
      <form onSubmit={(event) => void submit(event)} className="space-y-4" noValidate>
        <FormMessage>{login.error?.message}</FormMessage>

        <TextField
          label="Username"
          name="username"
          value={username}
          onChange={(event) => {
            setUsername(event.target.value);
          }}
          autoComplete="username"
          autoFocus
          required
          disabled={login.isPending}
        />

        <TextField
          label="Password"
          name="password"
          type="password"
          value={password}
          onChange={(event) => {
            setPassword(event.target.value);
          }}
          autoComplete="current-password"
          required
          disabled={login.isPending}
        />

        <Button type="submit" fullWidth disabled={login.isPending}>
          {login.isPending ? 'Signing in' : 'Sign in'}
        </Button>
      </form>
    </AuthLayout>
  );
}
