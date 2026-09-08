import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';

import { inventory, setInventory } from '@/test/msw/handlers';
import { makeDetail, makeDevice } from '@/test/msw/inventoryApi';
import { renderApp } from '@/test/renderApp';

const deviceId = 'device-1';

describe('adding a device', () => {
  it('sends what was typed and lands on the new device', async () => {
    setInventory({});

    const user = userEvent.setup();
    const { router } = renderApp('/devices/new');

    await user.type(await screen.findByLabelText('Hostname'), 'edge-sw-09');
    await user.type(screen.getByLabelText('Primary IP address'), '10.0.9.9');
    await user.selectOptions(screen.getByLabelText('Role'), 'Switch');
    await user.type(screen.getByLabelText('Tags'), 'Edge, edge , access');

    await user.click(screen.getByRole('button', { name: 'Add device' }));

    await waitFor(() => {
      expect(writeTo('POST', '/devices')).toEqual(
        expect.objectContaining({
          hostname: 'edge-sw-09',
          primaryIpAddress: '10.0.9.9',
          role: 'Switch',
          tags: ['Edge', 'edge', 'access'],
        }),
      );
    });

    await waitFor(() => {
      expect(router.state.location.pathname).toMatch(/^\/devices\//);
    });
  });

  /** DESIGN.md §8: the button names the action and the toast keeps the word. */
  it('confirms with the same word the button used', async () => {
    setInventory({});

    const user = userEvent.setup();

    renderApp('/devices/new');

    await user.type(await screen.findByLabelText('Hostname'), 'edge-sw-09');
    await user.type(screen.getByLabelText('Primary IP address'), '10.0.9.9');
    await user.click(screen.getByRole('button', { name: 'Add device' }));

    expect(await screen.findByText('Device added')).toBeVisible();
  });

  /** An empty optional box is an absent value, not an empty string stored as one. */
  it('sends nothing rather than an empty string for a box left blank', async () => {
    setInventory({});

    const user = userEvent.setup();

    renderApp('/devices/new');

    await user.type(await screen.findByLabelText('Hostname'), 'edge-sw-09');
    await user.type(screen.getByLabelText('Primary IP address'), '10.0.9.9');
    await user.click(screen.getByRole('button', { name: 'Add device' }));

    await waitFor(() => {
      expect(inventory.writes[0]?.body).toEqual(
        expect.objectContaining({ model: null, notes: null, owner: null }),
      );
    });
  });

  /** The 409 the API answers when a live device already holds the address (WP-1.1). */
  it('puts a duplicate-address refusal beside the address field', async () => {
    setInventory({
      devices: [makeDevice({ id: deviceId, primaryIpAddress: '10.0.0.1' })],
      detail: new Map([[deviceId, makeDetail({ id: deviceId })]]),
    });

    const user = userEvent.setup();

    renderApp('/devices/new');

    await user.type(await screen.findByLabelText('Hostname'), 'edge-sw-09');
    await user.type(screen.getByLabelText('Primary IP address'), '10.0.0.1');
    await user.click(screen.getByRole('button', { name: 'Add device' }));

    // The server's own wording, beside the field it is about.
    expect(await screen.findByText('Another device is already at 10.0.0.1.')).toBeVisible();
    expect(screen.getByLabelText('Primary IP address')).toHaveAttribute('aria-invalid', 'true');
  });

  /**
   * WP-1.1 keeps `state` off both request shapes: reachability is something NetShield observes
   * and WP-1.4 owns every transition. A control for it would be a rule waiting to be forgotten.
   */
  it('offers no control for the device state', async () => {
    setInventory({});

    renderApp('/devices/new');

    await screen.findByLabelText('Hostname');

    expect(screen.queryByLabelText('State')).not.toBeInTheDocument();
  });
});

describe('editing a device', () => {
  it('opens with the stored values and saves the change', async () => {
    setInventory({
      devices: [makeDevice({ id: deviceId })],
      detail: new Map([[deviceId, makeDetail({ id: deviceId })]]),
    });

    const user = userEvent.setup();

    renderApp(`/devices/${deviceId}?tab=settings`);

    const owner = await screen.findByLabelText('Owner');

    expect(owner).toHaveValue('Network team');

    await user.clear(owner);
    await user.type(owner, 'Platform team');
    await user.click(screen.getByRole('button', { name: 'Save device' }));

    await waitFor(() => {
      expect(writeTo('PUT', `/devices/${deviceId}`)).toEqual(
        expect.objectContaining({ owner: 'Platform team' }),
      );
    });

    expect(await screen.findByText('Device saved')).toBeVisible();
  });
});

describe('removing a device', () => {
  /** DESIGN.md §6: anything irreversible requires a typed confirmation. */
  it('refuses to remove until the device is named', async () => {
    setInventory({
      devices: [makeDevice({ id: deviceId })],
      detail: new Map([[deviceId, makeDetail({ id: deviceId })]]),
    });

    const user = userEvent.setup();

    renderApp(`/devices/${deviceId}?tab=settings`);

    await user.click(await screen.findByRole('button', { name: 'Remove device' }));

    const dialog = await screen.findByRole('dialog', { name: 'Remove device' });
    const confirm = screen.getAllByRole('button', { name: 'Remove device' }).at(-1);

    expect(confirm).toBeDisabled();

    await user.type(screen.getByLabelText(/Type/), 'wrong-name');

    expect(confirm).toBeDisabled();
    expect(inventory.writes).toHaveLength(0);
    expect(dialog).toBeVisible();
  });

  it('removes the device once its name is typed, and returns to the list', async () => {
    setInventory({
      devices: [makeDevice({ id: deviceId })],
      detail: new Map([[deviceId, makeDetail({ id: deviceId })]]),
    });

    const user = userEvent.setup();
    const { router } = renderApp(`/devices/${deviceId}?tab=settings`);

    await user.click(await screen.findByRole('button', { name: 'Remove device' }));
    await user.type(screen.getByLabelText(/Type/), 'core-sw-01');

    const confirm = screen.getAllByRole('button', { name: 'Remove device' }).at(-1);

    if (confirm === undefined) {
      throw new Error('The confirmation dialog has no confirm button.');
    }

    expect(confirm).toBeEnabled();

    await user.click(confirm);

    await waitFor(() => {
      expect(
        inventory.writes.some(
          (write) => write.method === 'DELETE' && write.path === `/devices/${deviceId}`,
        ),
      ).toBe(true);
    });

    await waitFor(() => {
      expect(router.state.location.pathname).toBe('/devices');
    });
  });
});

/**
 * The write the SPA made to a given route, or nothing. Reading it back this way keeps the
 * matchers out of an object literal, where they would be `any` and the lint would say so.
 */
function writeTo(method: string, path: string): unknown {
  return inventory.writes.find((write) => write.method === method && write.path === path)?.body;
}
