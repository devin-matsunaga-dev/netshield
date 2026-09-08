import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';

import { api, inventory, setInventory } from '@/test/msw/handlers';
import {
  makeDetail,
  makeDevice,
  makeFingerprint,
  makeInterface,
  makeReachability,
} from '@/test/msw/inventoryApi';
import { renderApp } from '@/test/renderApp';

const deviceId = 'device-1';

/** One device, fingerprinted, probed and with two ports. */
function aFingerprintedDevice(
  overrides: {
    fingerprint?: Partial<ReturnType<typeof makeFingerprint>>;
    reachability?: Partial<ReturnType<typeof makeReachability>> | null;
  } = {},
) {
  return setInventory({
    devices: [makeDevice({ id: deviceId })],
    detail: new Map([[deviceId, makeDetail({ id: deviceId })]]),
    fingerprints: new Map([[deviceId, makeFingerprint({ deviceId, ...overrides.fingerprint })]]),
    reachability:
      overrides.reachability === null
        ? // An empty map is "nothing has probed this device", which the overview has to say.
          new Map<string, ReturnType<typeof makeReachability>>()
        : new Map([[deviceId, makeReachability({ deviceId, ...overrides.reachability })]]),
    interfaces: new Map([
      [
        deviceId,
        [
          makeInterface({ id: 'if-1', ifIndex: 1, name: 'Gi1/0/1' }),
          makeInterface({
            id: 'if-2',
            ifIndex: 2,
            name: 'Gi1/0/2',
            adminStatus: 'Up',
            operStatus: 'LowerLayerDown',
            speedBitsPerSecond: 10_000_000_000,
          }),
        ],
      ],
    ]),
  });
}

describe('the device detail screen', () => {
  it('opens on the overview with the device named and its state shown', async () => {
    aFingerprintedDevice();

    renderApp(`/devices/${deviceId}`);

    expect(await screen.findByRole('heading', { name: 'core-sw-01' })).toBeVisible();

    // Once in the page header and once in the inventory panel below it.
    expect(screen.getAllByText('10.0.0.1').length).toBeGreaterThan(0);
    expect(await screen.findByRole('heading', { name: 'Inventory' })).toBeVisible();
  });

  /**
   * The gap this package closed. Every one of these was on `device_reachability` and reachable
   * only from the database until WP-1.7 gave it a route.
   */
  it('shows the evidence behind the state, not only the state', async () => {
    aFingerprintedDevice({ reachability: { lastRttMilliseconds: 4.25, lastLossPercent: 25 } });

    renderApp(`/devices/${deviceId}`);

    expect(await screen.findByText('4.3 ms')).toBeVisible();
    expect(screen.getByText('25%')).toBeVisible();
  });

  /**
   * The member that matters most. A failed probe leaves the state alone by design (WP-1.4), so
   * without this the device reads as confidently Online on evidence that stopped arriving.
   */
  it('says when the last probe could not be performed', async () => {
    aFingerprintedDevice({
      reachability: { lastError: 'no ICMP socket could be opened.', state: 'Online' },
    });

    renderApp(`/devices/${deviceId}`);

    expect(await screen.findByText(/The last probe could not be performed/)).toBeVisible();
    expect(screen.getByText(/no ICMP socket could be opened/)).toBeVisible();
  });

  it('says so plainly when nothing has probed the device yet', async () => {
    aFingerprintedDevice({ reachability: null });

    renderApp(`/devices/${deviceId}`);

    expect(await screen.findByText(/Nothing has probed this device yet/)).toBeVisible();
  });

  it('moves between tabs and puts the tab in the URL', async () => {
    aFingerprintedDevice();

    const user = userEvent.setup();
    const { router } = renderApp(`/devices/${deviceId}`);

    await user.click(await screen.findByRole('tab', { name: 'Interfaces' }));

    await waitFor(() => {
      expect(router.state.location.search).toEqual({ tab: 'interfaces' });
    });

    expect(await screen.findByRole('table', { name: 'Interfaces' })).toBeVisible();
  });

  it('opens straight onto a tab named in the URL', async () => {
    aFingerprintedDevice();

    renderApp(`/devices/${deviceId}?tab=fingerprint`);

    expect(await screen.findByText('What the walk read')).toBeVisible();
  });

  it('falls back to the overview when the URL names a tab that does not exist', async () => {
    aFingerprintedDevice();

    renderApp(`/devices/${deviceId}?tab=nonsense`);

    expect(await screen.findByRole('heading', { name: 'Inventory' })).toBeVisible();
  });

  describe('the fingerprint tab', () => {
    it('shows what the walk read', async () => {
      aFingerprintedDevice();

      renderApp(`/devices/${deviceId}?tab=fingerprint`);

      expect(await screen.findByText('1.3.6.1.4.1.9.1.2494')).toBeVisible();
      expect(screen.getByText('Cisco IOS Software, Version 17.9.4')).toBeVisible();
      expect(screen.getByText('Rack 3')).toBeVisible();
    });

    /**
     * SPEC.md §4 requires a generic-SNMP device's reduced feature set to be *clearly labelled in
     * the UI*. WP-1.5 recorded the fact; this is the label.
     */
    it('labels a generic-SNMP device as reduced capability and says what is unavailable', async () => {
      aFingerprintedDevice({
        fingerprint: { vendor: 'GenericSnmp', reducedCapability: true },
      });

      renderApp(`/devices/${deviceId}?tab=fingerprint`);

      expect(await screen.findByText('Reduced capability')).toBeVisible();
      expect(screen.getByText(/Config backup, drift detection and compliance/)).toBeVisible();
    });

    it('does not label a device the walk recognised fully', async () => {
      aFingerprintedDevice();

      renderApp(`/devices/${deviceId}?tab=fingerprint`);

      await screen.findByText('What the walk read');

      expect(screen.queryByText('Reduced capability')).not.toBeInTheDocument();
    });

    it('names the fields an operator has overruled', async () => {
      aFingerprintedDevice({ fingerprint: { overriddenFields: ['model', 'serialNumber'] } });

      renderApp(`/devices/${deviceId}?tab=fingerprint`);

      expect(await screen.findByText('Overridden by an operator')).toBeVisible();
      expect(screen.getByText(/model, serial number/)).toBeVisible();
    });

    it('says why the last walk failed without hiding what an earlier one found', async () => {
      aFingerprintedDevice({ fingerprint: { lastError: 'SNMP timeout after 5s.' } });

      renderApp(`/devices/${deviceId}?tab=fingerprint`);

      expect(await screen.findByText(/The last walk could not be performed/)).toBeVisible();
      // The facts an earlier walk established are still on the screen.
      expect(screen.getByText('1.3.6.1.4.1.9.1.2494')).toBeVisible();
    });

    it('offers a walk when nothing has ever fingerprinted the device', async () => {
      setInventory({
        devices: [makeDevice({ id: deviceId })],
        detail: new Map([[deviceId, makeDetail({ id: deviceId })]]),
      });

      renderApp(`/devices/${deviceId}?tab=fingerprint`);

      expect(await screen.findByText('This device has not been fingerprinted.')).toBeVisible();
      expect(screen.getByRole('button', { name: 'Walk now' })).toBeVisible();
    });

    it('queues a walk when asked', async () => {
      aFingerprintedDevice();

      const user = userEvent.setup();

      renderApp(`/devices/${deviceId}?tab=fingerprint`);

      await user.click(await screen.findByRole('button', { name: 'Walk now' }));

      await waitFor(() => {
        expect(
          inventory.writes.some(
            (write) => write.method === 'POST' && write.path === `/devices/${deviceId}/walk`,
          ),
        ).toBe(true);
      });

      expect(await screen.findByText(/Walk queued/)).toBeVisible();
    });
  });

  describe('the interfaces tab', () => {
    it('names each status rather than reporting an IF-MIB integer', async () => {
      aFingerprintedDevice();

      renderApp(`/devices/${deviceId}?tab=interfaces`);

      const table = await screen.findByRole('table', { name: 'Interfaces' });

      expect(within(table).getByText('Lower layer down')).toBeVisible();
      expect(within(table).getAllByText('Up').length).toBeGreaterThan(0);
    });

    it('writes a speed as a person would', async () => {
      aFingerprintedDevice();

      renderApp(`/devices/${deviceId}?tab=interfaces`);

      const table = await screen.findByRole('table', { name: 'Interfaces' });

      expect(within(table).getByText('1 Gbit/s')).toBeVisible();
      expect(within(table).getByText('10 Gbit/s')).toBeVisible();
    });

    it('says what to do when no walk has read the interface table', async () => {
      setInventory({
        devices: [makeDevice({ id: deviceId })],
        detail: new Map([[deviceId, makeDetail({ id: deviceId })]]),
      });

      renderApp(`/devices/${deviceId}?tab=interfaces`);

      expect(await screen.findByText('No interfaces recorded.')).toBeVisible();
    });
  });

  describe('what a session may see', () => {
    it('offers the credentials and settings tabs to an administrator', async () => {
      aFingerprintedDevice();

      renderApp(`/devices/${deviceId}`);

      expect(await screen.findByRole('tab', { name: 'Credentials' })).toBeVisible();
      expect(screen.getByRole('tab', { name: 'Settings' })).toBeVisible();
    });

    /**
     * Hiding is presentation and never the boundary — the API refuses either way
     * (ARCHITECTURE.md §8). It spares a reader a refusal they could not have predicted.
     */
    it('offers neither to a read-only session', async () => {
      aFingerprintedDevice();
      signInReadOnly();

      renderApp(`/devices/${deviceId}`);

      await screen.findByRole('tab', { name: 'Overview' });

      expect(screen.queryByRole('tab', { name: 'Credentials' })).not.toBeInTheDocument();
      expect(screen.queryByRole('tab', { name: 'Settings' })).not.toBeInTheDocument();
    });

    it('lands a read-only session on the overview even when the URL names a hidden tab', async () => {
      aFingerprintedDevice();
      signInReadOnly();

      renderApp(`/devices/${deviceId}?tab=settings`);

      expect(await screen.findByRole('heading', { name: 'Inventory' })).toBeVisible();
      expect(screen.queryByText('Remove device')).not.toBeInTheDocument();
    });
  });
});

function signInReadOnly(): void {
  api.user = {
    id: '019226b4-0000-7000-8000-000000000002',
    username: 'viewer',
    displayName: 'Ben Okri',
    role: 'ReadOnly',
    mustChangePassword: false,
    permissions: ['InventoryRead'],
  };
}
