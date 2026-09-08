import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';

import { credentials, readOnlyUser, resetApi, setCredentials } from '@/test/msw/handlers';
import { makeProfile, snmpProfileId, sshProfileId } from '@/test/msw/credentialsApi';
import { renderApp } from '@/test/renderApp';

function someProfiles() {
  return setCredentials({
    profiles: [
      makeProfile({
        id: snmpProfileId,
        name: 'Lab switches SNMP',
        kind: 'SnmpV2c',
        deviceCount: 3,
      }),
      makeProfile({
        id: sshProfileId,
        name: 'Backup SSH',
        kind: 'SshKey',
        username: 'backup',
        deviceCount: 0,
      }),
    ],
  });
}

/**
 * The open dialog.
 *
 * Queries inside a form are scoped to it rather than to the page, because the page genuinely has
 * two of several things — a "Kind" filter and a "Kind" field, an "Add profile" button that opens
 * the form and one that submits it. That is not an ambiguity to design away: DESIGN.md §8 wants a
 * button to keep its word through the flow, and a `<dialog>` traps focus, so a reader is only
 * ever in one of the two contexts.
 */
function dialog() {
  return within(screen.getByRole('dialog'));
}

/** The body of the last write to a path, so a test can see exactly what was sent. */
function writeTo(method: string, path: string): unknown {
  return credentials.writes.findLast((write) => write.method === method && write.path === path)
    ?.body;
}

describe('the credential profile list', () => {
  it('shows each profile with its kind, username and how many devices use it', async () => {
    someProfiles();

    renderApp('/devices/credentials');

    expect(await screen.findByText('Lab switches SNMP')).toBeVisible();

    const table = screen.getByRole('table', { name: /Credential profiles/ });
    const row = within(table).getByRole('row', { name: /Lab switches SNMP/ });

    expect(within(row).getByText('SNMP v2c')).toBeVisible();
    expect(within(row).getByText('3')).toBeVisible();
    expect(within(table).getByText('backup')).toBeVisible();
  });

  it('narrows by kind and by name', async () => {
    someProfiles();

    renderApp('/devices/credentials');

    await screen.findByText('Lab switches SNMP');

    await userEvent.selectOptions(screen.getByLabelText('Kind'), 'SshKey');

    await waitFor(() => {
      expect(screen.queryByText('Lab switches SNMP')).not.toBeInTheDocument();
    });

    expect(screen.getByText('Backup SSH')).toBeVisible();
  });

  it('says what to do when no profile exists', async () => {
    setCredentials();

    renderApp('/devices/credentials');

    expect(await screen.findByText('No credential profiles yet.')).toBeVisible();
    expect(screen.getByText(/NetShield needs a credential/)).toBeVisible();
  });

  it('offers a retry when the list cannot be loaded', async () => {
    setCredentials({ failList: true });

    renderApp('/devices/credentials');

    expect(await screen.findByText('The credential profiles could not be loaded.')).toBeVisible();
    expect(screen.getByRole('button', { name: 'Try again' })).toBeVisible();
  });
});

describe('creating a credential profile', () => {
  it('asks only for the members the chosen kind needs', async () => {
    setCredentials();

    renderApp('/devices/credentials');

    await userEvent.click(await screen.findByRole('button', { name: 'Add profile' }));

    // SNMP v2c is a community string and nothing else — no username, no algorithms.
    expect(dialog().getByLabelText('Community string')).toBeVisible();
    expect(dialog().queryByLabelText('Username')).not.toBeInTheDocument();
    expect(dialog().queryByLabelText('Authentication algorithm')).not.toBeInTheDocument();

    await userEvent.selectOptions(dialog().getByLabelText('Kind'), 'SnmpV3');

    expect(dialog().getByLabelText('Username')).toBeVisible();
    expect(dialog().getByLabelText('Authentication password')).toBeVisible();
    expect(dialog().getByLabelText('Privacy password')).toBeVisible();
    expect(dialog().queryByLabelText('Community string')).not.toBeInTheDocument();
  });

  it('drops the privacy password when the profile authenticates only', async () => {
    // WP-1.2 made `None` a member rather than an absent algorithm, because "this profile
    // authenticates and does not encrypt" is a decision somebody made.
    setCredentials();

    renderApp('/devices/credentials');

    await userEvent.click(await screen.findByRole('button', { name: 'Add profile' }));
    await userEvent.selectOptions(dialog().getByLabelText('Kind'), 'SnmpV3');
    await userEvent.selectOptions(dialog().getByLabelText('Privacy algorithm'), 'None');

    expect(dialog().queryByLabelText('Privacy password')).not.toBeInTheDocument();
  });

  it('sends the secret exactly once, in the request body', async () => {
    setCredentials();

    renderApp('/devices/credentials');

    await userEvent.click(await screen.findByRole('button', { name: 'Add profile' }));
    await userEvent.type(dialog().getByLabelText('Name'), 'Lab switches SNMP');
    await userEvent.type(dialog().getByLabelText('Community string'), 's3cret-community');
    await userEvent.click(dialog().getByRole('button', { name: 'Add profile' }));

    await waitFor(() => {
      expect(writeTo('POST', '/credential-profiles')).toMatchObject({
        name: 'Lab switches SNMP',
        kind: 'SnmpV2c',
        material: { community: 's3cret-community' },
      });
    });

    expect(await screen.findByText('Profile added')).toBeVisible();
  });

  it('clears what was typed under a kind when the kind changes', async () => {
    // The secret members belong to the kind. Carrying a community string into an SSH profile
    // would send the server a member it refuses, and would keep a secret in state under a form
    // that no longer shows it.
    setCredentials();

    renderApp('/devices/credentials');

    await userEvent.click(await screen.findByRole('button', { name: 'Add profile' }));
    await userEvent.type(dialog().getByLabelText('Community string'), 'typed-then-abandoned');
    await userEvent.selectOptions(dialog().getByLabelText('Kind'), 'SshPassword');
    await userEvent.type(dialog().getByLabelText('Name'), 'Backup SSH');
    await userEvent.type(dialog().getByLabelText('Username'), 'backup');
    await userEvent.type(dialog().getByLabelText('Password'), 'new-password');
    await userEvent.click(dialog().getByRole('button', { name: 'Add profile' }));

    await waitFor(() => {
      expect(writeTo('POST', '/credential-profiles')).toMatchObject({
        kind: 'SshPassword',
        material: { password: 'new-password' },
      });
    });

    expect(JSON.stringify(writeTo('POST', '/credential-profiles'))).not.toContain(
      'typed-then-abandoned',
    );
  });

  it('renders a kind rule the server refuses beside the form rather than as a field error', async () => {
    // WP-1.2 settled that "this kind requires a community string" is a 422 from the handler
    // rather than a 400 from a validator, because it is a fact about the profile's stored kind.
    setCredentials({
      nextRefusal: {
        status: 422,
        code: 'credential.missing-material',
        detail: 'An SNMPv2c profile needs a community string.',
      },
    });

    renderApp('/devices/credentials');

    await userEvent.click(await screen.findByRole('button', { name: 'Add profile' }));
    await userEvent.type(dialog().getByLabelText('Name'), 'Broken');
    await userEvent.click(dialog().getByRole('button', { name: 'Add profile' }));

    expect(await screen.findByText('An SNMPv2c profile needs a community string.')).toBeVisible();
  });
});

describe('editing a credential profile', () => {
  it('will not let the kind be changed', async () => {
    // The kind decides what the sealed blob contains, so a profile whose kind changed would hold
    // material describing a protocol it no longer claims to be for (WP-1.2).
    someProfiles();

    renderApp('/devices/credentials');

    await userEvent.click(
      await screen.findByRole('button', { name: 'Actions for Lab switches SNMP' }),
    );
    await userEvent.click(screen.getByRole('menuitem', { name: 'Edit profile' }));

    expect(dialog().getByLabelText('Kind')).toBeDisabled();
    expect(dialog().getByLabelText('Kind')).toHaveValue('SNMP v2c');
  });

  it('asks for no secret, because there is none to show and none to send', async () => {
    someProfiles();

    renderApp('/devices/credentials');

    await userEvent.click(
      await screen.findByRole('button', { name: 'Actions for Lab switches SNMP' }),
    );
    await userEvent.click(screen.getByRole('menuitem', { name: 'Edit profile' }));

    expect(dialog().queryByLabelText('Community string')).not.toBeInTheDocument();
  });

  it('sends the name and username and nothing about the secret', async () => {
    someProfiles();

    renderApp('/devices/credentials');

    await userEvent.click(
      await screen.findByRole('button', { name: 'Actions for Lab switches SNMP' }),
    );
    await userEvent.click(screen.getByRole('menuitem', { name: 'Edit profile' }));

    await userEvent.clear(dialog().getByLabelText('Name'));
    await userEvent.type(dialog().getByLabelText('Name'), 'Lab switches SNMP v2');
    await userEvent.click(dialog().getByRole('button', { name: 'Save profile' }));

    await waitFor(() => {
      expect(writeTo('PUT', `/credential-profiles/${snmpProfileId}`)).toEqual({
        name: 'Lab switches SNMP v2',
        description: null,
        username: null,
      });
    });
  });
});

describe('replacing a credential', () => {
  it('asks for the new value and never for the old one', async () => {
    someProfiles();

    renderApp('/devices/credentials');

    await userEvent.click(
      await screen.findByRole('button', { name: 'Actions for Lab switches SNMP' }),
    );
    await userEvent.click(screen.getByRole('menuitem', { name: 'Replace credential' }));

    expect(dialog().getByLabelText('Community string')).toHaveValue('');
    expect(dialog().queryByLabelText(/current/i)).not.toBeInTheDocument();
    expect(dialog().getByText(/cannot show you the current one/)).toBeVisible();
  });

  it('warns how many devices the change reaches', async () => {
    someProfiles();

    renderApp('/devices/credentials');

    await userEvent.click(
      await screen.findByRole('button', { name: 'Actions for Lab switches SNMP' }),
    );
    await userEvent.click(screen.getByRole('menuitem', { name: 'Replace credential' }));

    expect(dialog().getByText(/This profile reaches 3 devices/)).toBeVisible();
  });

  it('sends the new value to the material route alone', async () => {
    someProfiles();

    renderApp('/devices/credentials');

    await userEvent.click(
      await screen.findByRole('button', { name: 'Actions for Lab switches SNMP' }),
    );
    await userEvent.click(screen.getByRole('menuitem', { name: 'Replace credential' }));

    await userEvent.type(dialog().getByLabelText('Community string'), 'rotated-community');
    await userEvent.click(dialog().getByRole('button', { name: 'Replace credential' }));

    await waitFor(() => {
      expect(writeTo('PUT', `/credential-profiles/${snmpProfileId}/material`)).toEqual({
        material: { community: 'rotated-community' },
      });
    });

    // The whole-resource route must not have been touched: a secret has one way in.
    expect(writeTo('PUT', `/credential-profiles/${snmpProfileId}`)).toBeUndefined();
    expect(await screen.findByText('Credential replaced')).toBeVisible();
  });
});

describe('removing a credential profile', () => {
  it('requires the profile name typed, and says what will lose it', async () => {
    someProfiles();

    renderApp('/devices/credentials');

    await userEvent.click(
      await screen.findByRole('button', { name: 'Actions for Lab switches SNMP' }),
    );
    await userEvent.click(screen.getByRole('menuitem', { name: 'Remove profile' }));

    expect(dialog().getByText(/3 devices are assigned this profile/)).toBeVisible();

    const confirm = dialog().getByRole('button', { name: 'Remove profile' });

    expect(confirm).toBeDisabled();

    await userEvent.type(dialog().getByLabelText(/Type .* to confirm/), 'Lab switches SNMP');

    expect(confirm).toBeEnabled();

    await userEvent.click(confirm);

    await waitFor(() => {
      expect(credentials.writes.some((write) => write.method === 'DELETE')).toBe(true);
    });

    expect(await screen.findByText('Profile removed')).toBeVisible();
  });
});

describe('who may reach the credential screen', () => {
  it('does not offer it to a session that cannot manage credentials', async () => {
    // WP-1.2 put even the list of profile names behind `CredentialsManage`, because it says which
    // accounts NetShield holds passwords for. Hiding the link is presentation; the API refuses
    // the routes regardless, which is what `CredentialAuthorizationTests` asserts on that side.
    resetApi({ user: readOnlyUser });
    setCredentials();

    renderApp('/devices');

    await screen.findByRole('heading', { name: 'Devices' });

    expect(screen.queryByRole('link', { name: 'Credentials' })).not.toBeInTheDocument();
  });

  it('offers it to an administrator', async () => {
    setCredentials();

    renderApp('/devices');

    await screen.findByRole('heading', { name: 'Devices' });

    expect(screen.getByRole('link', { name: 'Credentials' })).toBeVisible();
  });
});
