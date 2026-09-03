import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';

import { api, resetApi } from '@/test/msw/handlers';
import { renderApp } from '@/test/renderApp';

describe('an expired session', () => {
  it('refreshes silently and the reader never sees the sign-in page', async () => {
    // The session cookie has lapsed; the refresh cookie has not. This is the ordinary case for
    // an operator who left a screen open over lunch.
    resetApi({ sessionValid: false, refreshAllowed: true });

    renderApp('/devices');

    expect(await screen.findByRole('heading', { level: 1, name: 'Devices' })).toBeVisible();
    expect(screen.queryByRole('heading', { name: 'Sign in' })).not.toBeInTheDocument();
  });

  it('refreshes once, not once per attempt', async () => {
    resetApi({ sessionValid: false, refreshAllowed: true });

    renderApp('/devices');

    await screen.findByRole('heading', { level: 1, name: 'Devices' });

    expect(api.calls.filter((call) => call === 'POST /refresh')).toHaveLength(1);
  });

  it('replays the call that found the session gone', async () => {
    resetApi({ sessionValid: false, refreshAllowed: true });

    renderApp('/devices');

    await screen.findByRole('heading', { level: 1, name: 'Devices' });

    // The first /me is refused, the refresh succeeds, and the same /me is sent again — which is
    // what makes the recovery invisible rather than a reload.
    expect(api.calls).toEqual(['GET /me', 'POST /refresh', 'GET /me']);
  });

  it('redirects to sign-in when the refresh is refused too', async () => {
    resetApi({ sessionValid: false, refreshAllowed: false });

    renderApp('/devices');

    expect(await screen.findByRole('heading', { level: 1, name: 'Sign in' })).toBeVisible();
    expect(api.calls.filter((call) => call === 'POST /refresh')).toHaveLength(1);
  });

  it('keeps the route it was on, so signing in returns there', async () => {
    resetApi({ sessionValid: false, refreshAllowed: false });

    const { router } = renderApp('/reports/bandwidth');

    await screen.findByRole('heading', { level: 1, name: 'Sign in' });

    await waitFor(() => {
      expect(router.state.location.search).toEqual({ redirect: '/reports/bandwidth' });
    });
  });

  it('never tries to refresh a refused sign-in', async () => {
    const user = userEvent.setup();
    resetApi({ sessionValid: false, refreshAllowed: false });

    renderApp('/overview');

    // Getting to the sign-in page costs one refresh attempt; this test is about what the
    // sign-in itself does.
    await screen.findByRole('heading', { level: 1, name: 'Sign in' });
    api.calls.length = 0;

    await user.type(screen.getByLabelText('Username'), 'admin');
    await user.type(screen.getByLabelText('Password'), 'not-the-password');
    await user.click(screen.getByRole('button', { name: 'Sign in' }));

    await screen.findByRole('alert');

    // A 401 from the sign-in endpoint means the password was wrong, not that a session lapsed.
    // Refreshing there would be a round trip that can only fail, and on a loop.
    expect(api.calls).toEqual(['POST /login']);
  });
});
