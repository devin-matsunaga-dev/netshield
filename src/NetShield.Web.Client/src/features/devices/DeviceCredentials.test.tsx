import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';

import type { Schemas } from '@/api/types';
import { inventory, setInventory } from '@/test/msw/handlers';
import { makeDetail, makeDevice } from '@/test/msw/inventoryApi';
import { renderApp } from '@/test/renderApp';

const deviceId = 'device-1';

const snmp: Schemas['CredentialProfileSummary'] = {
  id: 'profile-snmp',
  name: 'Core SNMP v3',
  kind: 'SnmpV3',
  username: 'netshield-ro',
  deviceCount: 4,
  materialUpdatedAt: '2026-09-08T09:00:00.000Z',
  updatedAt: '2026-09-08T09:00:00.000Z',
};

const ssh: Schemas['CredentialProfileSummary'] = {
  id: 'profile-ssh',
  name: 'Backup SSH',
  kind: 'SshKey',
  username: 'backup',
  deviceCount: 0,
  materialUpdatedAt: '2026-09-08T09:00:00.000Z',
  updatedAt: '2026-09-08T09:00:00.000Z',
};

function aDeviceWithProfiles(assigned: Schemas['CredentialProfileSummary'][]) {
  return setInventory({
    devices: [makeDevice({ id: deviceId })],
    detail: new Map([[deviceId, makeDetail({ id: deviceId })]]),
    profiles: [snmp, ssh],
    deviceProfiles: new Map([[deviceId, assigned]]),
  });
}

describe('assigning credential profiles to a device', () => {
  it('shows every profile with the assigned ones already chosen', async () => {
    aDeviceWithProfiles([snmp]);

    renderApp(`/devices/${deviceId}?tab=credentials`);

    expect(await screen.findByRole('checkbox', { name: /Core SNMP v3/ })).toBeChecked();
    expect(screen.getByRole('checkbox', { name: /Backup SSH/ })).not.toBeChecked();
  });

  /**
   * WP-1.2: the API never returns a secret in any response shape, and a structural test over the
   * OpenAPI document fails the build if one ever appears. This is the screen half of that — the
   * name, kind and username are what a profile is here, and nothing more.
   */
  it('shows a profile by name, kind and username, and nothing that could be a secret', async () => {
    aDeviceWithProfiles([]);

    renderApp(`/devices/${deviceId}?tab=credentials`);

    await screen.findByText('Core SNMP v3');

    expect(screen.getByText('netshield-ro')).toBeVisible();
    expect(screen.getByText('SNMP v3')).toBeVisible();
    expect(screen.queryByLabelText(/password/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/community/i)).not.toBeInTheDocument();
  });

  /**
   * Whole-set replacement rather than add and remove: the request says what is true afterwards,
   * so two operators editing one device cannot interleave into a set neither asked for.
   */
  it('sends the whole set when the assignment is saved', async () => {
    aDeviceWithProfiles([snmp]);

    const user = userEvent.setup();

    renderApp(`/devices/${deviceId}?tab=credentials`);

    await user.click(await screen.findByRole('checkbox', { name: /Backup SSH/ }));
    await user.click(screen.getByRole('button', { name: 'Save assignment' }));

    await waitFor(() => {
      expect(writeTo('PUT', `/devices/${deviceId}/credential-profiles`)).toEqual({
        credentialProfileIds: ['profile-snmp', 'profile-ssh'],
      });
    });

    expect(await screen.findByText('Credential profiles saved')).toBeVisible();
  });

  it('sends an empty set when every profile is unchecked', async () => {
    aDeviceWithProfiles([snmp]);

    const user = userEvent.setup();

    renderApp(`/devices/${deviceId}?tab=credentials`);

    await user.click(await screen.findByRole('checkbox', { name: /Core SNMP v3/ }));
    await user.click(screen.getByRole('button', { name: 'Save assignment' }));

    await waitFor(() => {
      expect(writeTo('PUT', `/devices/${deviceId}/credential-profiles`)).toEqual({
        credentialProfileIds: [],
      });
    });
  });

  it('offers nothing to save until something is changed', async () => {
    aDeviceWithProfiles([snmp]);

    renderApp(`/devices/${deviceId}?tab=credentials`);

    await screen.findByText('Core SNMP v3');

    expect(screen.queryByRole('button', { name: 'Save assignment' })).not.toBeInTheDocument();
  });

  it('abandons a change without sending it', async () => {
    aDeviceWithProfiles([snmp]);

    const user = userEvent.setup();

    renderApp(`/devices/${deviceId}?tab=credentials`);

    await user.click(await screen.findByRole('checkbox', { name: /Backup SSH/ }));
    await user.click(screen.getByRole('button', { name: 'Cancel' }));

    expect(inventory.writes).toHaveLength(0);
    expect(screen.getByRole('checkbox', { name: /Backup SSH/ })).not.toBeChecked();
  });

  it('says what to do when no profile exists to assign', async () => {
    setInventory({
      devices: [makeDevice({ id: deviceId })],
      detail: new Map([[deviceId, makeDetail({ id: deviceId })]]),
      profiles: [],
    });

    renderApp(`/devices/${deviceId}?tab=credentials`);

    expect(await screen.findByText('No credential profiles exist yet.')).toBeVisible();
    expect(screen.getByText(/NetShield needs a credential to walk or poll anything/)).toBeVisible();
  });
});

/**
 * The write the SPA made to a given route, or nothing. Reading it back this way keeps the
 * matchers out of an object literal, where they would be `any` and the lint would say so.
 */
function writeTo(method: string, path: string): unknown {
  return inventory.writes.find((write) => write.method === method && write.path === path)?.body;
}
