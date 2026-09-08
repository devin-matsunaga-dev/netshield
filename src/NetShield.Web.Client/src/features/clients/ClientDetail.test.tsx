import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';

import { laptopId, makeClientDetail, makeIpBinding, makePortBinding } from '@/test/msw/clientsApi';
import { setClients } from '@/test/msw/handlers';
import { renderApp } from '@/test/renderApp';

const earlier = '2026-09-08T06:00:00.000Z';
const later = '2026-09-08T09:00:00.000Z';

function aClient() {
  return setClients({
    detail: new Map([[laptopId, makeClientDetail()]]),
    ipHistory: new Map([
      [
        laptopId,
        [
          makeIpBinding({ id: 'binding-2', ipAddress: '10.10.0.21', observedFrom: later }),
          makeIpBinding({
            id: 'binding-1',
            ipAddress: '10.10.0.9',
            observedFrom: earlier,
            observedTo: later,
          }),
        ],
      ],
    ]),
    portHistory: new Map([
      [
        laptopId,
        [
          makePortBinding({ id: 'port-2', ifIndex: 1, interfaceName: 'Gi1/0/1' }),
          makePortBinding({
            id: 'port-1',
            ifIndex: 48,
            interfaceName: 'Te1/1/1',
            macCountOnPort: 214,
            observedFrom: earlier,
            observedTo: later,
          }),
        ],
      ],
    ]),
  });
}

describe('the client detail', () => {
  it('shows the hardware address as the identity', async () => {
    aClient();

    renderApp(`/clients/${laptopId}`);

    expect(await screen.findByRole('heading', { name: 'AA:BB:CC:00:00:21' })).toBeVisible();
  });

  it('shows every address and port that is open now', async () => {
    // Every one, not one of each: a client legitimately holds an IPv4 and an IPv6 address at
    // once, and is legitimately reported by its access switch and by every switch above it.
    aClient();

    renderApp(`/clients/${laptopId}`);

    expect(await screen.findByText('Addresses held now')).toBeVisible();
    expect(screen.getByText('Ports reporting it now')).toBeVisible();
    expect(screen.getByText('Gi1/0/1')).toBeVisible();
  });

  it('reads the learned-address count out as evidence about the port', async () => {
    aClient();

    renderApp(`/clients/${laptopId}`);

    expect(await screen.findByText('Access port — 1 address learned')).toBeVisible();
  });

  it('explains why several switches report one client', async () => {
    setClients({
      detail: new Map([
        [
          laptopId,
          makeClientDetail({
            portBindings: [
              makePortBinding({ id: 'port-a', ifIndex: 1, macCountOnPort: 1 }),
              makePortBinding({
                id: 'port-b',
                deviceId: '019226b4-1000-7000-8000-000000000002',
                deviceHostname: 'dist-sw-01',
                ifIndex: 48,
                interfaceName: 'Te1/1/1',
                macCountOnPort: 214,
              }),
            ],
          }),
        ],
      ]),
    });

    renderApp(`/clients/${laptopId}`);

    expect(await screen.findByText(/learned by every bridge on the path to it/)).toBeVisible();
    expect(screen.getByText('214 addresses learned on this port')).toBeVisible();
  });

  it('says so when a client holds no address', async () => {
    // Seen in a forwarding database and never in an ARP table. An ordinary state, not an error.
    setClients({
      detail: new Map([[laptopId, makeClientDetail({ ipBindings: [] })]]),
    });

    renderApp(`/clients/${laptopId}`);

    expect(await screen.findByText(/No address is currently bound to this client/)).toBeVisible();
  });

  it('shows the address history as closed intervals, newest first', async () => {
    aClient();

    renderApp(`/clients/${laptopId}?tab=addresses`);

    const table = await screen.findByRole('table', {
      name: /Every address this client has held/,
    });
    // Header, then the open interval, then the one it superseded.
    const [, current, superseded] = within(table).getAllByRole('row');

    if (current === undefined || superseded === undefined) {
      throw new Error('The address history should show two intervals.');
    }

    expect(within(current).getByText('10.10.0.21')).toBeVisible();
    expect(within(current).getByText('Still held')).toBeVisible();
    expect(within(superseded).getByText('10.10.0.9')).toBeVisible();
  });

  it('shows the port history with the VLAN and the addresses on each port', async () => {
    aClient();

    renderApp(`/clients/${laptopId}?tab=ports`);

    const table = await screen.findByRole('table', {
      name: /Every port that has reported this client/,
    });

    expect(within(table).getByText('Te1/1/1')).toBeVisible();
    expect(within(table).getByText('214')).toBeVisible();
    expect(within(table).getByText('Still reported')).toBeVisible();
  });

  it('says what to do when nothing has ever seen the client hold an address', async () => {
    setClients({ detail: new Map([[laptopId, makeClientDetail({ ipBindings: [] })]]) });

    renderApp(`/clients/${laptopId}?tab=addresses`);

    expect(
      await screen.findByText('This client has never been seen holding an address.'),
    ).toBeVisible();
  });

  it('moves between tabs and puts the tab in the address', async () => {
    aClient();

    const { router } = renderApp(`/clients/${laptopId}`);

    await userEvent.click(await screen.findByRole('tab', { name: 'Port history' }));

    expect(router.state.location.search).toMatchObject({ tab: 'ports' });
  });

  it('lands on the overview for a tab it does not know', async () => {
    aClient();

    renderApp(`/clients/${laptopId}?tab=melted`);

    expect(await screen.findByText('Identity')).toBeVisible();
  });

  it('offers a retry when the client cannot be loaded', async () => {
    setClients();

    renderApp(`/clients/${laptopId}`);

    expect(await screen.findByText('The client could not be loaded.')).toBeVisible();
    expect(screen.getByRole('button', { name: 'Try again' })).toBeVisible();
  });
});
