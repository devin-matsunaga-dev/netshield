import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';

import { makeProfile, snmpProfileId } from '@/test/msw/credentialsApi';
import { credentials, setCredentials } from '@/test/msw/handlers';
import { renderApp } from '@/test/renderApp';

/** A value distinctive enough that finding it anywhere is unambiguous. */
const secret = 'zzq-unmistakable-secret-9471';

/**
 * The guarantee this screen exists to keep, checked rather than asserted in a comment.
 *
 * WP-1.2 covers the server: `SecretRedactor` keeps secrets out of every log line, and
 * `ApiSecretExposureTests` walks every response schema in the committed OpenAPI document and
 * fails the build if a member appears that the redactor would blank. Neither of those can reach
 * the browser. This is the half that has to be kept by hand — a secret is typed here, and the
 * only places it may exist are the field the reader typed it into and the request body.
 *
 * Each test looks for the value somewhere it must never be, so a regression names the place.
 */
describe('a secret typed into the credential screen', () => {
  async function createAProfile() {
    setCredentials();

    const { queryClient, router } = renderApp('/devices/credentials');

    await userEvent.click(await screen.findByRole('button', { name: 'Add profile' }));

    const dialog = within(screen.getByRole('dialog'));

    await userEvent.type(dialog.getByLabelText('Name'), 'Lab switches SNMP');
    await userEvent.type(dialog.getByLabelText('Community string'), secret);
    await userEvent.click(dialog.getByRole('button', { name: 'Add profile' }));

    await waitFor(() => {
      expect(credentials.writes).not.toHaveLength(0);
    });

    return { queryClient, router };
  }

  it('reaches the API in the request body, which is the whole point', async () => {
    await createAProfile();

    expect(JSON.stringify(credentials.writes[0]?.body)).toContain(secret);
  });

  it('is not in the document once the form has closed', async () => {
    await createAProfile();

    await screen.findByText('Profile added');

    expect(document.body.textContent).not.toContain(secret);
    expect(document.body.innerHTML).not.toContain(secret);
  });

  it('is not in the URL at any point', async () => {
    const { router } = await createAProfile();

    expect(JSON.stringify(router.state.location)).not.toContain(secret);
  });

  it('is not in a query key or a cached value', async () => {
    // A query key lives for the life of the cache and is visible in every devtools panel. The
    // API returns no material, so nothing it answers with can carry one either — this is what
    // says the client does not put one there itself.
    const { queryClient } = await createAProfile();

    const cache = queryClient.getQueryCache().getAll();

    for (const entry of cache) {
      expect(JSON.stringify(entry.queryKey)).not.toContain(secret);
      expect(JSON.stringify(entry.state.data ?? null)).not.toContain(secret);
    }
  });

  it('is not left in browser storage', async () => {
    await createAProfile();

    expect(JSON.stringify(window.localStorage)).not.toContain(secret);
    expect(JSON.stringify(window.sessionStorage)).not.toContain(secret);
  });

  it('is masked while it is being typed', async () => {
    setCredentials();

    renderApp('/devices/credentials');

    await userEvent.click(await screen.findByRole('button', { name: 'Add profile' }));

    const field = within(screen.getByRole('dialog')).getByLabelText('Community string');

    expect(field).toHaveAttribute('type', 'password');
    // A browser offering to remember a device's SNMP community is a copy of it in a password
    // manager nobody decided to make.
    expect(field).toHaveAttribute('autocomplete', 'off');
  });

  it('is not prefilled when a credential is replaced, because there is nothing to prefill it with', async () => {
    setCredentials({ profiles: [makeProfile({ id: snmpProfileId, name: 'Lab switches SNMP' })] });

    renderApp('/devices/credentials');

    await userEvent.click(
      await screen.findByRole('button', { name: 'Actions for Lab switches SNMP' }),
    );
    await userEvent.click(screen.getByRole('menuitem', { name: 'Replace credential' }));

    expect(within(screen.getByRole('dialog')).getByLabelText('Community string')).toHaveValue('');
  });

  it('is not asked for when a profile is merely edited', async () => {
    // A whole-resource PUT carries no material, so an edit form that collected one would be
    // gathering a secret it had nowhere to send.
    setCredentials({ profiles: [makeProfile({ id: snmpProfileId, name: 'Lab switches SNMP' })] });

    renderApp('/devices/credentials');

    await userEvent.click(
      await screen.findByRole('button', { name: 'Actions for Lab switches SNMP' }),
    );
    await userEvent.click(screen.getByRole('menuitem', { name: 'Edit profile' }));

    const dialog = within(screen.getByRole('dialog'));

    expect(dialog.queryByLabelText('Community string')).not.toBeInTheDocument();
    expect(dialog.queryByLabelText(/password/i)).not.toBeInTheDocument();
  });

  it('is never rendered by the list, because the API never returns one', async () => {
    setCredentials({ profiles: [makeProfile({ name: 'Lab switches SNMP' })] });

    renderApp('/devices/credentials');

    await screen.findByText('Lab switches SNMP');

    const table = screen.getByRole('table', { name: /Credential profiles/ });

    expect(within(table).queryByText(/community/i)).not.toBeInTheDocument();
    expect(table.textContent).not.toContain(secret);
  });
});
