import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';

import { signInRefused } from '@/features/auth/api/loginMutation';
import { api, resetApi } from '@/test/msw/handlers';
import { renderApp } from '@/test/renderApp';

/** Signs in through the form, the way a person does. */
async function signIn(user: ReturnType<typeof userEvent.setup>) {
  await user.type(await screen.findByLabelText('Username'), 'admin');
  await user.type(screen.getByLabelText('Password'), api.password);
  await user.click(screen.getByRole('button', { name: 'Sign in' }));
}

describe('the session guard', () => {
  it('sends an unauthenticated visit to any route to sign in', async () => {
    resetApi({ sessionValid: false, refreshAllowed: false });

    renderApp('/devices');

    expect(await screen.findByRole('heading', { level: 1, name: 'Sign in' })).toBeVisible();
    expect(screen.queryByRole('navigation', { name: 'Main' })).not.toBeInTheDocument();
  });

  it('returns to the route that was asked for once the sign-in succeeds', async () => {
    const user = userEvent.setup();
    resetApi({ sessionValid: false, refreshAllowed: false });

    renderApp('/logs');

    await signIn(user);

    expect(await screen.findByRole('heading', { level: 1, name: 'Logs' })).toBeVisible();
  });

  it('keeps the reader on the sign-in page when the credentials are refused', async () => {
    const user = userEvent.setup();
    resetApi({ sessionValid: false, refreshAllowed: false });

    renderApp('/logs');

    await user.type(await screen.findByLabelText('Username'), 'admin');
    await user.type(screen.getByLabelText('Password'), 'not-the-password');
    await user.click(screen.getByRole('button', { name: 'Sign in' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(signInRefused);
    expect(screen.getByRole('heading', { level: 1, name: 'Sign in' })).toBeVisible();
  });

  it('says nothing about which half of the credentials was wrong', async () => {
    const user = userEvent.setup();
    resetApi({ user: null, sessionValid: false, refreshAllowed: false });

    renderApp('/overview');

    await user.type(await screen.findByLabelText('Username'), 'nobody-by-that-name');
    await user.type(screen.getByLabelText('Password'), 'anything-at-all');
    await user.click(screen.getByRole('button', { name: 'Sign in' }));

    // The API answers an unknown username and a wrong password identically (WP-0.4); the client
    // has to be just as uninformative or it undoes that from this side.
    expect(await screen.findByRole('alert')).toHaveTextContent(signInRefused);
  });

  it('lets a signed-in visit through to the route it asked for', async () => {
    renderApp('/devices');

    expect(await screen.findByRole('heading', { level: 1, name: 'Devices' })).toBeVisible();
  });

  it('sends someone who is already signed in away from the sign-in page', async () => {
    // Reached from inside the application, which is where the session is in the cache. The
    // sign-in route deliberately never asks the API who the caller is — see its comment.
    const { router } = renderApp('/devices');
    await screen.findByRole('heading', { level: 1, name: 'Devices' });

    await router.navigate({ to: '/login' });

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Network overview' }),
    ).toBeVisible();
  });

  it('refuses to return to an address outside the application', async () => {
    const user = userEvent.setup();
    resetApi({ sessionValid: false, refreshAllowed: false });

    // An absolute URL in the return path would make the sign-in page an open redirect.
    renderApp('/login?redirect=https://example.invalid/steal');

    await signIn(user);

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Network overview' }),
    ).toBeVisible();
  });

  it('shows a not-found page inside the shell for an address nothing serves', async () => {
    renderApp('/no-such-screen');

    expect(await screen.findByRole('heading', { level: 1, name: 'Page not found' })).toBeVisible();
    expect(screen.getByRole('navigation', { name: 'Main' })).toBeInTheDocument();
  });

  it('sends an unauthenticated visit to an address nothing serves to sign in', async () => {
    resetApi({ sessionValid: false, refreshAllowed: false });

    renderApp('/no-such-screen');

    expect(await screen.findByRole('heading', { level: 1, name: 'Sign in' })).toBeVisible();
  });

  it('ends the session and returns to sign-in on sign out', async () => {
    const user = userEvent.setup();

    renderApp('/overview');

    await user.click(await screen.findByRole('button', { name: 'Account menu for Ada Lovelace' }));
    await user.click(screen.getByRole('menuitem', { name: 'Sign out' }));

    expect(await screen.findByRole('heading', { level: 1, name: 'Sign in' })).toBeVisible();

    await waitFor(() => {
      expect(api.calls).toContain('POST /logout');
    });

    // Signing out asks the API once. A cache cleared while the shell was still mounted would
    // send a `/auth/me` after it, and a refused refresh after that — an audit row for a refresh
    // nobody attempted.
    expect(api.calls.filter((call) => call === 'POST /refresh')).toEqual([]);
  });
});
