import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';

import { setInventory } from '@/test/msw/handlers';
import {
  makeDetail,
  makeDevice,
  makePort,
  makePortClient,
  makePortNeighbor,
} from '@/test/msw/inventoryApi';
import { renderApp } from '@/test/renderApp';

const deviceId = '019226b4-1000-7000-8000-000000000001';
const portsTab = `/devices/${deviceId}?tab=ports`;

function anEstate(ports: ReturnType<typeof makePort>[]) {
  return setInventory({
    devices: [makeDevice({ id: deviceId })],
    detail: new Map([[deviceId, makeDetail({ id: deviceId })]]),
    ports: new Map([[deviceId, ports]]),
  });
}

/** Opens one port's detail panel by its `ifIndex`, as a reader does. */
async function openDetail(name: string) {
  const table = await screen.findByRole('table', { name: 'Ports' });

  await userEvent.click(within(table).getByRole('button', { name: `Detail for port ${name}` }));
}

describe('the device port view', () => {
  it('shows the host on a port with one host', async () => {
    anEstate([
      makePort({
        ifIndex: 2,
        name: 'Gi1/0/2',
        role: 'Access',
        roleReason: 'Endpoints',
        learnedAddressCount: 1,
        clientCount: 1,
        clientsListed: true,
        clients: [makePortClient()],
      }),
    ]);

    renderApp(portsTab);

    const table = await screen.findByRole('table', { name: 'Ports' });

    expect(within(table).getByText('Gi1/0/2')).toBeVisible();
    expect(within(table).getByText('Access')).toBeVisible();
    expect(within(table).getByText('1 host')).toBeVisible();

    await openDetail('Gi1/0/2');

    expect(screen.getByRole('link', { name: 'AA:BB:CC:00:00:01' })).toBeVisible();
    expect(screen.getByText('10.20.0.50')).toBeVisible();
    expect(screen.getByText('VLAN 30')).toBeVisible();
  });

  it('shows the topology edge on a port facing another switch', async () => {
    anEstate([
      makePort({
        role: 'Uplink',
        roleReason: 'ManagedDevice',
        neighbors: [
          makePortNeighbor({
            deviceId: '019226b4-1000-7000-8000-000000000002',
            managed: true,
            hostname: 'core-sw-1',
            systemName: 'core-sw-1',
            systemDescription: null,
            remotePortName: 'Ethernet1',
            confidence: 'Confirmed',
            bidirectional: true,
            sources: ['Lldp', 'Cdp'],
            capabilities: ['Bridge', 'Router'],
          }),
        ],
        clientsListed: false,
      }),
    ]);

    renderApp(portsTab);

    const table = await screen.findByRole('table', { name: 'Ports' });

    expect(within(table).getByText(/core-sw-1/)).toBeVisible();

    await openDetail('Gi1/0/1');

    // The far end is a device NetShield monitors, so it is reachable rather than only named.
    expect(screen.getByRole('link', { name: 'core-sw-1' })).toHaveAttribute(
      'href',
      '/devices/019226b4-1000-7000-8000-000000000002',
    );
    expect(screen.getByText('Ethernet1')).toBeVisible();
    expect(screen.getByText('Confirmed')).toBeVisible();
    expect(screen.getByText(/LLDP, CDP \(both ends\)/)).toBeVisible();
    expect(screen.getByText('Faces a monitored device.')).toBeVisible();
  });

  it('names an access point and says its capability in words', async () => {
    anEstate([
      makePort({
        ifIndex: 3,
        name: 'Gi1/0/3',
        role: 'Access',
        roleReason: 'Endpoints',
        neighbors: [makePortNeighbor()],
      }),
    ]);

    renderApp(portsTab);

    await openDetail('Gi1/0/3');

    // Scoped to the panel: the row above also names the access point, which is the point of the
    // summary column and would otherwise make this assertion ambiguous.
    const announced = within(screen.getByRole('region', { name: 'Announced' }));

    expect(announced.getByText('ap-floor-1')).toBeVisible();
    expect(announced.getByText('ArubaOS (MODEL: 535), Version 8.10.0.6')).toBeVisible();

    // The bitmap in words. "WlanAccessPoint" is a wire name, not a label (DESIGN.md §4).
    expect(announced.getByText('Access point')).toBeVisible();
    expect(announced.getByText('Switch')).toBeVisible();
    expect(announced.getByText('Not monitored')).toBeVisible();
  });

  it('says how many addresses an uplink carries rather than listing them as occupants', async () => {
    // The criterion that matters most: a MAC is learned by every bridge on the path to it, so
    // an uplink's forwarding database holds every host beyond it. Listing them here would say
    // two hundred hosts are plugged into one cable.
    anEstate([
      makePort({
        role: 'Uplink',
        roleReason: 'LearnedAddressCount',
        learnedAddressCount: 200,
        clientCount: 187,
        clientsListed: false,
        clients: [],
      }),
    ]);

    renderApp(portsTab);

    const table = await screen.findByRole('table', { name: 'Ports' });

    expect(within(table).getByText('Carrying 200 addresses')).toBeVisible();

    await openDetail('Gi1/0/1');

    expect(screen.getByText(/Carries 200 addresses for what is behind it/)).toBeVisible();
    expect(screen.getByText(/counted rather than listed/)).toBeVisible();

    // And nothing on the panel is a client row.
    expect(screen.queryByRole('link', { name: /AA:BB:CC/ })).not.toBeInTheDocument();
  });

  it('says why a port was classified as it was, not only what it decided', async () => {
    anEstate([
      makePort({
        role: 'Uplink',
        roleReason: 'InfrastructureNeighbor',
        neighbors: [
          makePortNeighbor({
            systemName: 'desk-switch',
            systemDescription: null,
            capabilities: ['Bridge'],
          }),
        ],
        clientsListed: false,
      }),
    ]);

    renderApp(portsTab);

    await openDetail('Gi1/0/1');

    expect(screen.getByText('The far end reports itself as network infrastructure.')).toBeVisible();
  });

  it('lists a port no walk has fingerprinted, and says so', async () => {
    anEstate([
      makePort({
        ifIndex: 48,
        name: null,
        description: null,
        interfaceKnown: false,
        adminStatus: 'Unknown',
        operStatus: 'Unknown',
        role: 'Access',
        roleReason: 'Endpoints',
        clientCount: 1,
        clients: [makePortClient()],
      }),
    ]);

    renderApp(portsTab);

    await openDetail('48');

    expect(screen.getByText(/No walk has recorded this interface/)).toBeVisible();
  });

  it('says an empty port is empty rather than leaving it blank', async () => {
    anEstate([makePort()]);

    renderApp(portsTab);

    const table = await screen.findByRole('table', { name: 'Ports' });

    expect(within(table).getByText('Empty')).toBeVisible();
    expect(within(table).getByText('Nothing observed')).toBeVisible();
  });

  it('says so when the device has no ports at all', async () => {
    anEstate([]);

    renderApp(portsTab);

    expect(await screen.findByText('No ports recorded.')).toBeVisible();
  });

  it('says what failed and offers a retry', async () => {
    anEstate([makePort()]);
    setInventory({
      devices: [makeDevice({ id: deviceId })],
      detail: new Map([[deviceId, makeDetail({ id: deviceId })]]),
      ports: new Map([[deviceId, [makePort()]]]),
      failPortList: true,
    });

    renderApp(portsTab);

    expect(await screen.findByText('The port list could not be loaded.')).toBeVisible();
    expect(screen.getByRole('button', { name: 'Try again' })).toBeVisible();
  });

  it('is reachable as a tab of its own beside Interfaces', async () => {
    anEstate([makePort()]);

    renderApp(`/devices/${deviceId}`);

    const ports = await screen.findByRole('tab', { name: 'Ports' });

    expect(screen.getByRole('tab', { name: 'Interfaces' })).toBeVisible();

    await userEvent.click(ports);

    expect(await screen.findByRole('table', { name: 'Ports' })).toBeVisible();
  });
});
