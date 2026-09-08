import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, it } from 'vitest';

import { expectNoAccessibilityViolations } from '@/test/axe';
import { makeProfile, snmpProfileId } from '@/test/msw/credentialsApi';
import { setCredentials } from '@/test/msw/handlers';
import { renderApp } from '@/test/renderApp';

function someProfiles() {
  return setCredentials({
    profiles: [
      makeProfile({ id: snmpProfileId, name: 'Lab switches SNMP', deviceCount: 3 }),
      makeProfile({
        id: 'profile-v3',
        name: 'Core SNMP v3',
        kind: 'SnmpV3',
        username: 'netshield-ro',
      }),
    ],
  });
}

/**
 * `axe` over every state this screen can be in.
 *
 * The dialogs are where it earns its place: a `<dialog>` that never opened exposes no role at
 * all, and a secret field with no bound label is invisible to anything that is not a pair of
 * eyes — which on a form where the value cannot be read back is worse than usual.
 */
describe('the credential screens', () => {
  it('the list has no accessibility violations', async () => {
    someProfiles();

    const { container } = renderApp('/devices/credentials');

    await screen.findByText('Lab switches SNMP');

    await expectNoAccessibilityViolations(container);
  });

  it('the empty list has none', async () => {
    setCredentials();

    const { container } = renderApp('/devices/credentials');

    await screen.findByText('No credential profiles yet.');

    await expectNoAccessibilityViolations(container);
  });

  it('the error state has none', async () => {
    setCredentials({ failList: true });

    const { container } = renderApp('/devices/credentials');

    await screen.findByText('The credential profiles could not be loaded.');

    await expectNoAccessibilityViolations(container);
  });

  it('the create form has none, for every kind', async () => {
    setCredentials();

    const { container } = renderApp('/devices/credentials');

    await userEvent.click(await screen.findByRole('button', { name: 'Add profile' }));

    const dialog = () => within(screen.getByRole('dialog'));

    await expectNoAccessibilityViolations(container);

    // SNMP v3 draws the most: a username, two algorithm selects and two secrets.
    await userEvent.selectOptions(dialog().getByLabelText('Kind'), 'SnmpV3');
    await expectNoAccessibilityViolations(container);

    // An SSH key is the only multi-line secret, and the only one that is not an <input>.
    await userEvent.selectOptions(dialog().getByLabelText('Kind'), 'SshKey');
    await expectNoAccessibilityViolations(container);
  });

  it('the rotation dialog has none', async () => {
    someProfiles();

    const { container } = renderApp('/devices/credentials');

    await userEvent.click(
      await screen.findByRole('button', { name: 'Actions for Lab switches SNMP' }),
    );
    await userEvent.click(screen.getByRole('menuitem', { name: 'Replace credential' }));

    await expectNoAccessibilityViolations(container);
  });

  it('the removal confirmation has none', async () => {
    someProfiles();

    const { container } = renderApp('/devices/credentials');

    await userEvent.click(
      await screen.findByRole('button', { name: 'Actions for Lab switches SNMP' }),
    );
    await userEvent.click(screen.getByRole('menuitem', { name: 'Remove profile' }));

    await expectNoAccessibilityViolations(container);
  });
});
