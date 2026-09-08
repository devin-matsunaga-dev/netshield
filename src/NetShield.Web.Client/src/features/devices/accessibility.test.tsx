import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, it } from 'vitest';

import { expectNoAccessibilityViolations } from '@/test/axe';
import { setInventory } from '@/test/msw/handlers';
import {
  makeCandidate,
  makeDetail,
  makeDevice,
  makeFingerprint,
  makeInterface,
  makeReachability,
  makeRun,
  makeRunDetail,
  makeSeed,
} from '@/test/msw/inventoryApi';
import { renderApp } from '@/test/renderApp';

const deviceId = 'device-1';

function anEstate() {
  return setInventory({
    devices: [makeDevice({ id: deviceId })],
    detail: new Map([[deviceId, makeDetail({ id: deviceId })]]),
    fingerprints: new Map([[deviceId, makeFingerprint({ deviceId })]]),
    reachability: new Map([[deviceId, makeReachability({ deviceId })]]),
    interfaces: new Map([[deviceId, [makeInterface()]]]),
    candidates: [makeCandidate()],
    runs: [makeRun()],
    runDetail: new Map([['019226b4-4000-7000-8000-000000000001', makeRunDetail()]]),
    runHosts: new Map([
      [
        '019226b4-4000-7000-8000-000000000001',
        [
          {
            id: 'host-1',
            runId: '019226b4-4000-7000-8000-000000000001',
            address: '192.0.2.37',
            rttMilliseconds: 1.2,
            outcome: 'NewCandidate' as const,
            candidateId: null,
            deviceId: null,
            observedAt: '2026-09-08T09:00:00.000Z',
          },
        ],
      ],
    ]),
    seeds: [makeSeed()],
    ignores: [],
    profiles: [
      {
        id: 'profile-1',
        name: 'Core SNMP',
        kind: 'SnmpV3',
        username: 'netshield-ro',
        deviceCount: 1,
        materialUpdatedAt: '2026-09-08T09:00:00.000Z',
        updatedAt: '2026-09-08T09:00:00.000Z',
      },
    ],
    deviceProfiles: new Map([[deviceId, []]]),
  });
}

/**
 * CONVENTIONS.md §6 asks for keyboard reach, a visible focus ring and a label on every icon-only
 * control. These cover the half a machine can see, on every screen this package adds.
 */
describe('the inventory screens', () => {
  it('has no accessibility violation on the device list', async () => {
    anEstate();

    const { container } = renderApp('/devices');
    await screen.findByText('core-sw-01');

    await expectNoAccessibilityViolations(container);
  });

  it('has no accessibility violation on the device list with an empty state', async () => {
    setInventory({ devices: [] });

    const { container } = renderApp('/devices');
    await screen.findByText('No devices yet.');

    await expectNoAccessibilityViolations(container);
  });

  it('has no accessibility violation on the device list with an error showing', async () => {
    setInventory({ failDeviceList: true });

    const { container } = renderApp('/devices');
    await screen.findByRole('alert');

    await expectNoAccessibilityViolations(container);
  });

  it('has no accessibility violation on the device detail', async () => {
    anEstate();

    const { container } = renderApp(`/devices/${deviceId}`);
    await screen.findByRole('heading', { name: 'core-sw-01' });

    await expectNoAccessibilityViolations(container);
  });

  it('has no accessibility violation on the interfaces tab', async () => {
    anEstate();

    const { container } = renderApp(`/devices/${deviceId}?tab=interfaces`);
    await screen.findByRole('table', { name: 'Interfaces' });

    await expectNoAccessibilityViolations(container);
  });

  it('has no accessibility violation on the credentials tab', async () => {
    anEstate();

    const { container } = renderApp(`/devices/${deviceId}?tab=credentials`);
    await screen.findByText('Core SNMP');

    await expectNoAccessibilityViolations(container);
  });

  it('has no accessibility violation on the add form', async () => {
    setInventory({});

    const { container } = renderApp('/devices/new');
    await screen.findByLabelText('Hostname');

    await expectNoAccessibilityViolations(container);
  });

  it('has no accessibility violation on the delete confirmation', async () => {
    anEstate();

    const user = userEvent.setup();
    const { container } = renderApp(`/devices/${deviceId}?tab=settings`);

    await user.click(await screen.findByRole('button', { name: 'Remove device' }));
    await screen.findByRole('dialog', { name: 'Remove device' });

    await expectNoAccessibilityViolations(container);
  });

  it('has no accessibility violation on the discovery review', async () => {
    anEstate();

    const { container } = renderApp('/devices/discovery');
    await screen.findByText('192.0.2.37');

    await expectNoAccessibilityViolations(container);
  });

  it('has no accessibility violation on a discovery run', async () => {
    anEstate();

    const { container } = renderApp('/devices/discovery/runs/019226b4-4000-7000-8000-000000000001');
    await screen.findByRole('table', { name: 'Hosts that answered' });

    await expectNoAccessibilityViolations(container);
  });

  it('has no accessibility violation on the seeds tab', async () => {
    anEstate();

    const { container } = renderApp('/devices/discovery?tab=seeds');
    await screen.findByRole('table', { name: 'Discovery seeds' });

    await expectNoAccessibilityViolations(container);
  });
});
