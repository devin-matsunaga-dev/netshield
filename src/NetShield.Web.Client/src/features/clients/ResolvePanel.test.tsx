import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';

import { laptopId, makeResolution, switchId } from '@/test/msw/clientsApi';
import { setClients } from '@/test/msw/handlers';
import { renderApp } from '@/test/renderApp';

/**
 * The panel that answers "what held this address, then".
 *
 * This is the only way a person can check the handover behaviour by hand rather than by reading
 * a test, so what these assert is that the answer is *legible*: which kind of asset, which one,
 * and the interval the answer came out of — because a resolution nobody can check is one nobody
 * should trust.
 */
describe('resolving an address', () => {
  async function ask(address: string) {
    await userEvent.type(screen.getByLabelText('IP address'), address);
    await userEvent.click(screen.getByRole('button', { name: 'Resolve' }));
  }

  it('answers with the client that held the address', async () => {
    setClients({
      resolutions: new Map([['10.10.0.21', makeResolution()]]),
    });

    renderApp('/clients');

    await screen.findByText('Resolve an address');
    await ask('10.10.0.21');

    // The badge names the kind; the row beside it names which one. The two say different
    // things, which is why the labels are not the kind repeated.
    expect(await screen.findByText('Client')).toBeVisible();
    expect(screen.getByText('Hardware address')).toBeVisible();
    expect(screen.getByRole('link', { name: 'AA:BB:CC:00:00:21' })).toBeVisible();
  });

  it('answers with the device when one holds the address', async () => {
    setClients({
      resolutions: new Map([
        [
          '10.0.0.1',
          makeResolution({
            ipAddress: '10.0.0.1',
            kind: 'Device',
            deviceId: switchId,
            deviceHostname: 'core-sw-01',
            clientId: null,
            macAddress: null,
            observedFrom: null,
            observedTo: null,
          }),
        ],
      ]),
    });

    renderApp('/clients');

    await screen.findByText('Resolve an address');
    await ask('10.0.0.1');

    expect(await screen.findByText('Device')).toBeVisible();
    expect(screen.getByText('Device name')).toBeVisible();
    expect(screen.getByRole('link', { name: 'core-sw-01' })).toBeVisible();
  });

  it('shows the interval the answer came out of', async () => {
    setClients({
      resolutions: new Map([
        [
          '10.10.0.21',
          makeResolution({
            observedFrom: '2026-09-08T06:00:00.000Z',
            observedTo: '2026-09-08T09:00:00.000Z',
          }),
        ],
      ]),
    });

    renderApp('/clients');

    await screen.findByText('Resolve an address');
    await ask('10.10.0.21');

    expect(await screen.findByText('Held from')).toBeVisible();
    expect(screen.getByText('Held until')).toBeVisible();
  });

  it('says an open interval is still held rather than showing a blank', async () => {
    setClients({ resolutions: new Map([['10.10.0.21', makeResolution()]]) });

    renderApp('/clients');

    await screen.findByText('Resolve an address');
    await ask('10.10.0.21');

    expect(await screen.findByText('Still held')).toBeVisible();
  });

  it('says plainly when nothing held the address', async () => {
    setClients({
      resolutions: new Map([
        [
          '203.0.113.9',
          makeResolution({
            ipAddress: '203.0.113.9',
            kind: 'Unresolved',
            clientId: null,
            macAddress: null,
            observedFrom: null,
            observedTo: null,
          }),
        ],
      ]),
    });

    renderApp('/clients');

    await screen.findByText('Resolve an address');
    await ask('203.0.113.9');

    expect(
      await screen.findByText(/Nothing in the inventory held this address then/),
    ).toBeVisible();
  });

  it('treats a refusal as an ordinary answer rather than an error state', async () => {
    // Typing something that is not an address, or a moment that has not happened, are both
    // things the reader can act on. Neither deserves an error panel.
    setClients();

    renderApp('/clients');

    await screen.findByText('Resolve an address');
    await ask('not-an-address');

    expect(await screen.findByText(/That is not an address NetShield can resolve/)).toBeVisible();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('will not ask about nothing', async () => {
    setClients();

    renderApp('/clients');

    await screen.findByText('Resolve an address');

    expect(screen.getByRole('button', { name: 'Resolve' })).toBeDisabled();
  });

  it('opens the client the answer named', async () => {
    setClients({ resolutions: new Map([['10.10.0.21', makeResolution()]]) });

    const { router } = renderApp('/clients');

    await screen.findByText('Resolve an address');
    await ask('10.10.0.21');

    await userEvent.click(await screen.findByRole('link', { name: 'AA:BB:CC:00:00:21' }));

    await waitFor(() => {
      expect(router.state.location.pathname).toBe(`/clients/${laptopId}`);
    });
  });
});
