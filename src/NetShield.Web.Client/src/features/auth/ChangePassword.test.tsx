import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';

import { api, resetApi, testUser } from '@/test/msw/handlers';
import { renderApp } from '@/test/renderApp';

/** A session that owes a password change, which is what a seeded administrator's first one is. */
function owingAChange() {
  return resetApi({ user: { ...testUser, mustChangePassword: true } });
}

/** Fills the three fields and submits. */
async function change(
  user: ReturnType<typeof userEvent.setup>,
  current: string,
  next: string,
  confirmation = next,
) {
  await user.type(await screen.findByLabelText('Current password'), current);
  await user.type(screen.getByLabelText('New password'), next);
  await user.type(screen.getByLabelText('Confirm new password'), confirmation);
  await user.click(screen.getByRole('button', { name: 'Change password' }));
}

describe('a session that owes a password change', () => {
  it('is sent to the change screen from any route it asks for', async () => {
    owingAChange();

    renderApp('/devices');

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Change your password' }),
    ).toBeVisible();
  });

  it('is sent there from sign-in too, rather than to the route it asked for', async () => {
    const user = userEvent.setup();
    owingAChange();
    api.sessionValid = false;
    api.refreshAllowed = false;

    renderApp('/logs');

    await user.type(await screen.findByLabelText('Username'), 'admin');
    await user.type(screen.getByLabelText('Password'), api.password);
    await user.click(screen.getByRole('button', { name: 'Sign in' }));

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Change your password' }),
    ).toBeVisible();
  });

  it('reaches the application once the change is accepted', async () => {
    const user = userEvent.setup();
    owingAChange();

    renderApp('/overview');
    await screen.findByRole('heading', { level: 1, name: 'Change your password' });

    await change(user, api.password, 'Longer-Passphrase-99');

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Network overview' }),
    ).toBeVisible();
  });

  it('stays on the screen and says so when the current password is wrong', async () => {
    const user = userEvent.setup();
    owingAChange();

    renderApp('/overview');
    await screen.findByRole('heading', { level: 1, name: 'Change your password' });

    await change(user, 'not-my-password', 'Longer-Passphrase-99');

    // A 422 rather than a 401, so a typo here does not end a session that is perfectly valid
    // (WP-0.4). The message belongs beside the field that was wrong.
    expect(await screen.findByText('That is not your current password.')).toBeVisible();
    expect(screen.getByRole('heading', { level: 1, name: 'Change your password' })).toBeVisible();
    expect(screen.getByLabelText('Current password')).toBeInvalid();
  });

  it('shows the policy the server applied, in the server’s own words', async () => {
    const user = userEvent.setup();
    owingAChange();

    renderApp('/overview');
    await screen.findByRole('heading', { level: 1, name: 'Change your password' });

    await change(user, api.password, 'short');

    expect(await screen.findByText('It must be at least 12 characters long.')).toBeVisible();
    expect(screen.getByLabelText('New password')).toBeInvalid();
  });

  it('says so when the new password is the one already in use', async () => {
    const user = userEvent.setup();
    owingAChange();

    renderApp('/overview');
    await screen.findByRole('heading', { level: 1, name: 'Change your password' });

    await change(user, api.password, api.password);

    expect(
      await screen.findByText('The new password must be different from your current one.'),
    ).toBeVisible();
  });

  it('catches a mistyped confirmation without asking the server', async () => {
    const user = userEvent.setup();
    owingAChange();

    renderApp('/overview');
    await screen.findByRole('heading', { level: 1, name: 'Change your password' });
    api.calls.length = 0;

    await change(user, api.password, 'Longer-Passphrase-99', 'Longer-Passphrase-98');

    expect(await screen.findByText('The two new passwords do not match.')).toBeVisible();
    expect(api.calls).toEqual([]);
  });

  it('can sign out instead, which is the only other way off the screen', async () => {
    const user = userEvent.setup();
    owingAChange();

    renderApp('/overview');
    await screen.findByRole('heading', { level: 1, name: 'Change your password' });

    await user.click(screen.getByRole('button', { name: 'Sign out' }));

    expect(await screen.findByRole('heading', { level: 1, name: 'Sign in' })).toBeVisible();
  });
});

describe('a session that owes nothing', () => {
  it('is turned away from the change screen', async () => {
    renderApp('/change-password');

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Network overview' }),
    ).toBeVisible();
  });

  it('is sent to sign in if it has no session at all', async () => {
    resetApi({ sessionValid: false, refreshAllowed: false });

    renderApp('/change-password');

    expect(await screen.findByRole('heading', { level: 1, name: 'Sign in' })).toBeVisible();
  });
});
