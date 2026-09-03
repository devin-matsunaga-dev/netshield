import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';

import { expectNoAccessibilityViolations } from '@/test/axe';
import { resetApi, testUser } from '@/test/msw/handlers';
import { renderApp } from '@/test/renderApp';

describe('the signed-out screens', () => {
  it('has no accessibility violation on sign-in', async () => {
    resetApi({ sessionValid: false, refreshAllowed: false });

    const { container } = renderApp('/overview');
    await screen.findByRole('heading', { level: 1, name: 'Sign in' });

    await expectNoAccessibilityViolations(container);
  });

  it('has no accessibility violation on the forced password change', async () => {
    resetApi({ user: { ...testUser, mustChangePassword: true } });

    const { container } = renderApp('/overview');
    await screen.findByRole('heading', { level: 1, name: 'Change your password' });

    await expectNoAccessibilityViolations(container);
  });

  it('has no accessibility violation with a failure showing', async () => {
    const user = userEvent.setup();
    resetApi({ sessionValid: false, refreshAllowed: false });

    const { container } = renderApp('/overview');

    await user.type(await screen.findByLabelText('Username'), 'admin');
    await user.type(screen.getByLabelText('Password'), 'not-the-password');
    await user.click(screen.getByRole('button', { name: 'Sign in' }));
    await screen.findByRole('alert');

    await expectNoAccessibilityViolations(container);
  });

  it('reaches every control on the sign-in form from the keyboard', async () => {
    const user = userEvent.setup();
    resetApi({ sessionValid: false, refreshAllowed: false });

    renderApp('/overview');

    const username = await screen.findByLabelText('Username');

    // Autofocused, so the first thing a keyboard user does is type.
    expect(username).toHaveFocus();

    await user.tab();
    expect(screen.getByLabelText('Password')).toHaveFocus();

    await user.tab();
    expect(screen.getByRole('button', { name: 'Sign in' })).toHaveFocus();
  });

  it('names the field a rejection belongs to, so a screen reader hears it', async () => {
    const user = userEvent.setup();
    resetApi({ user: { ...testUser, mustChangePassword: true } });

    renderApp('/overview');
    await screen.findByRole('heading', { level: 1, name: 'Change your password' });

    await user.type(screen.getByLabelText('Current password'), 'not-my-password');
    await user.type(screen.getByLabelText('New password'), 'Longer-Passphrase-99');
    await user.type(screen.getByLabelText('Confirm new password'), 'Longer-Passphrase-99');
    await user.click(screen.getByRole('button', { name: 'Change password' }));

    const field = await screen.findByLabelText('Current password');

    expect(field).toBeInvalid();
    expect(field).toHaveAccessibleDescription('That is not your current password.');
  });
});

describe('the account menu', () => {
  it('has no accessibility violation when it is open', async () => {
    const user = userEvent.setup();

    const { container } = renderApp('/overview');

    await user.click(await screen.findByRole('button', { name: 'Account menu for Ada Lovelace' }));

    await expectNoAccessibilityViolations(container);
  });

  it('closes on Escape, so the keyboard is never trapped in it', async () => {
    const user = userEvent.setup();

    renderApp('/overview');

    const trigger = await screen.findByRole('button', { name: 'Account menu for Ada Lovelace' });
    await user.click(trigger);

    expect(screen.getByRole('menuitem', { name: 'Sign out' })).toBeVisible();

    await user.keyboard('{Escape}');

    expect(trigger).toHaveAttribute('aria-expanded', 'false');
  });
});
