import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';

import { sidebarCollapsedKey } from '@/lib/useSidebarCollapsed';
import { renderApp } from '@/test/renderApp';

describe('the sidebar', () => {
  it('marks the route you are on as the current page', async () => {
    renderApp('/devices');

    await waitFor(() => {
      expect(screen.getByRole('link', { name: 'Devices' })).toHaveAttribute('aria-current', 'page');
    });

    expect(screen.getByRole('link', { name: 'Clients' })).not.toHaveAttribute('aria-current');
  });

  it('collapses, and remembers that it is collapsed', async () => {
    const user = userEvent.setup();
    renderApp();

    await user.click(await screen.findByRole('button', { name: 'Collapse sidebar' }));

    expect(screen.getByRole('button', { name: 'Expand sidebar' })).toBeVisible();
    expect(window.localStorage.getItem(sidebarCollapsedKey)).toBe('true');
  });

  it('starts collapsed when that is what was remembered', async () => {
    window.localStorage.setItem(sidebarCollapsedKey, 'true');
    renderApp();

    expect(await screen.findByRole('button', { name: 'Expand sidebar' })).toBeVisible();
  });

  it('expands a section to show its destinations, and folds it away again', async () => {
    const user = userEvent.setup();
    renderApp();

    const section = await screen.findByRole('button', { name: 'Reports' });
    expect(section).toHaveAttribute('aria-expanded', 'false');

    await user.click(section);

    expect(section).toHaveAttribute('aria-expanded', 'true');
    expect(screen.getByRole('link', { name: 'Bandwidth' })).toBeVisible();

    await user.click(section);

    expect(section).toHaveAttribute('aria-expanded', 'false');
    expect(screen.getByRole('link', { name: 'Bandwidth', hidden: true })).not.toBeVisible();
  });

  it('opens the section holding the route you arrived on', async () => {
    renderApp('/administration/audit-log');

    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'Administration' })).toHaveAttribute(
        'aria-expanded',
        'true',
      );
    });

    expect(screen.getByRole('link', { name: 'Audit log' })).toHaveAttribute('aria-current', 'page');
  });

  it('opens the sidebar when a section is chosen while it is collapsed', async () => {
    const user = userEvent.setup();
    window.localStorage.setItem(sidebarCollapsedKey, 'true');
    renderApp();

    await user.click(await screen.findByRole('button', { name: 'Security' }));

    expect(screen.getByRole('button', { name: 'Collapse sidebar' })).toBeVisible();
    expect(screen.getByRole('link', { name: 'Posture' })).toBeVisible();
  });

  it('is reachable from the keyboard, in the order it is read in', async () => {
    const user = userEvent.setup();
    renderApp();

    const navigation = await screen.findByRole('navigation');
    const first = within(navigation).getByRole('link', { name: 'Overview' });

    first.focus();
    await user.tab();

    expect(within(navigation).getByRole('link', { name: 'Dashboard' })).toHaveFocus();
  });
});
