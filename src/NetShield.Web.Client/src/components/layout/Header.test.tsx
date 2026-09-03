import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';

import { themeKey } from '@/lib/useTheme';
import { renderApp } from '@/test/renderApp';

describe('the header', () => {
  it('focuses the search field on the keyboard shortcut the chip advertises', async () => {
    const user = userEvent.setup();
    renderApp();

    const search = await screen.findByRole('searchbox', {
      name: 'Search for devices, clients, alerts',
    });
    expect(search).not.toHaveFocus();

    await user.keyboard('{Meta>}k{/Meta}');

    expect(search).toHaveFocus();
  });

  it('names the person the session belongs to, read through the generated client', async () => {
    renderApp();

    const account = await screen.findByRole('button', {
      name: 'Account menu for Ada Lovelace',
    });

    expect(account).toBeVisible();
    expect(within(account).getByText('Administrator')).toBeVisible();
  });

  it('switches theme, and remembers which one', async () => {
    const user = userEvent.setup();
    renderApp();

    await user.click(await screen.findByRole('button', { name: 'Switch to light theme' }));

    await waitFor(() => {
      expect(document.documentElement.dataset['theme']).toBe('light');
    });
    expect(window.localStorage.getItem(themeKey)).toBe('light');

    await user.click(screen.getByRole('button', { name: 'Switch to dark theme' }));

    await waitFor(() => {
      expect(document.documentElement.dataset['theme']).toBe('dark');
    });
  });

  it('gives every icon-only control a name', async () => {
    renderApp();

    expect(await screen.findByRole('button', { name: 'Notifications' })).toBeVisible();
    expect(screen.getByRole('button', { name: 'Help' })).toBeVisible();
    expect(screen.getByRole('button', { name: 'Switch to light theme' })).toBeVisible();
  });
});
