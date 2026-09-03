import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';

import { navigation } from '@/app/navigation';
import { readOnlyUser, resetApi, testUser } from '@/test/msw/handlers';
import { renderApp } from '@/test/renderApp';

/** The sidebar's rows, whatever they happen to be for this session. */
async function sidebar() {
  return within(await screen.findByRole('navigation', { name: 'Main' }));
}

describe('the sidebar, for the session it belongs to', () => {
  it('shows an administrator every destination', async () => {
    renderApp('/overview');

    const nav = await sidebar();

    // Role queries rather than text: a section's children are in the DOM but hidden until it is
    // expanded, and `getByText` would count those too.
    for (const entry of navigation) {
      const role = entry.children === undefined ? 'link' : 'button';

      expect(nav.getByRole(role, { name: entry.label })).toBeInTheDocument();
    }
  });

  it('hides the write destinations from a read-only session', async () => {
    resetApi({ user: readOnlyUser });

    renderApp('/overview');

    const nav = await sidebar();

    // Policies is `PoliciesWrite`; Administration's children are `SystemAdminister` and
    // `AuditRead`. A read-only session holds none of the three.
    expect(nav.queryByRole('link', { name: 'Policies' })).not.toBeInTheDocument();
    expect(nav.queryByRole('button', { name: 'Administration' })).not.toBeInTheDocument();
  });

  it('still shows a read-only session everything it may read', async () => {
    resetApi({ user: readOnlyUser });

    renderApp('/overview');

    const nav = await sidebar();

    for (const label of ['Overview', 'Devices', 'Clients', 'Alerts', 'Logs', 'Compliance']) {
      expect(nav.getByRole('link', { name: label })).toBeInTheDocument();
    }
  });

  it('drops a section whose children the session cannot reach, rather than leaving a chevron', async () => {
    resetApi({
      user: { ...testUser, role: 'Operator', permissions: ['InventoryRead', 'ReportsRead'] },
    });

    renderApp('/overview');

    const nav = await sidebar();

    expect(nav.queryByRole('button', { name: 'Administration' })).not.toBeInTheDocument();
    expect(nav.getByRole('button', { name: 'Reports' })).toBeInTheDocument();
  });

  it('keeps only the children of a section that the session holds', async () => {
    const user = userEvent.setup();
    resetApi({
      user: { ...testUser, role: 'Operator', permissions: ['AuditRead'] },
    });

    renderApp('/overview');

    const nav = await sidebar();
    await user.click(nav.getByRole('button', { name: 'Administration' }));

    expect(nav.getByRole('link', { name: 'Audit log' })).toBeInTheDocument();
    expect(nav.queryByRole('link', { name: 'Users' })).not.toBeInTheDocument();
  });

  it('hides everything but the two entries a session needs no permission for', async () => {
    resetApi({ user: { ...testUser, role: 'ReadOnly', permissions: [] } });

    renderApp('/overview');

    const nav = await sidebar();

    // Overview and Dashboard name no permission: a session is enough to see them.
    expect(nav.getByRole('link', { name: 'Overview' })).toBeInTheDocument();
    expect(nav.getByRole('link', { name: 'Dashboard' })).toBeInTheDocument();
    expect(nav.queryByRole('link', { name: 'Devices' })).not.toBeInTheDocument();
  });

  it('does not block the route itself — hiding is presentation, the API is the boundary', async () => {
    resetApi({ user: readOnlyUser });

    // Typing the address still arrives. Nothing on this side may be the thing that refuses:
    // ARCHITECTURE.md §8 checks at the endpoint and again in the module.
    renderApp('/policies');

    expect(await screen.findByRole('heading', { level: 1, name: 'Policies' })).toBeVisible();
  });
});
