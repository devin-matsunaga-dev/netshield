import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';

import { makeDevice, makeDetail } from '@/test/msw/inventoryApi';
import { api, setInventory } from '@/test/msw/handlers';
import { renderApp } from '@/test/renderApp';

/** A page of devices, differing only in what a given test filters on. */
function anEstate() {
  return setInventory({
    devices: [
      makeDevice({
        id: 'device-1',
        hostname: 'core-sw-01',
        primaryIpAddress: '10.0.0.1',
        state: 'Online',
        vendor: 'CiscoIos',
        site: 'HQ',
        criticality: 'Critical',
      }),
      makeDevice({
        id: 'device-2',
        hostname: 'edge-fw-01',
        primaryIpAddress: '10.0.0.2',
        state: 'Offline',
        vendor: 'FortinetFortiOs',
        role: 'Firewall',
        site: 'Branch',
        criticality: 'High',
      }),
      makeDevice({
        id: 'device-3',
        hostname: 'ap-floor-2',
        primaryIpAddress: '10.0.0.3',
        state: 'Warning',
        vendor: 'GenericSnmp',
        role: 'AccessPoint',
        site: 'HQ',
        criticality: 'Low',
      }),
    ],
    detail: new Map([['device-1', makeDetail({ id: 'device-1' })]]),
  });
}

describe('the device list', () => {
  it('shows every device with its state', async () => {
    anEstate();

    renderApp('/devices');

    expect(await screen.findByText('core-sw-01')).toBeVisible();
    expect(screen.getByText('edge-fw-01')).toBeVisible();
    expect(screen.getByText('ap-floor-2')).toBeVisible();

    // The label, not only the colour — DESIGN.md §9.2 admits no colour as the only signal.
    // Scoped to the table: the state filter offers the same four words as options.
    const table = screen.getByRole('table', { name: 'Devices' });

    expect(within(table).getByText('Online')).toBeVisible();
    expect(within(table).getByText('Offline')).toBeVisible();
    expect(within(table).getByText('Warning')).toBeVisible();
  });

  it('says how many devices the inventory holds', async () => {
    anEstate();

    renderApp('/devices');

    expect(await screen.findByText('3 devices in the inventory.')).toBeVisible();
  });

  /** The contract types every number as `number | string`, so one device must not read "1 devices". */
  it('says "1 device" for a single one', async () => {
    setInventory({ devices: [makeDevice()] });

    renderApp('/devices');

    expect(await screen.findByText('1 device in the inventory.')).toBeVisible();
  });

  /**
   * The WP-1.7 criterion. A filter that lived in component state would be gone on the next
   * render of the page; one that lives in the address survives a refresh, the back button and a
   * pasted link.
   */
  it('reads its filters from the URL, so a pasted link arrives filtered', async () => {
    anEstate();

    renderApp('/devices?state=Offline');

    expect(await screen.findByText('edge-fw-01')).toBeVisible();
    expect(screen.queryByText('core-sw-01')).not.toBeInTheDocument();
    expect(screen.queryByText('ap-floor-2')).not.toBeInTheDocument();
  });

  it('writes a chosen filter into the URL', async () => {
    anEstate();

    const user = userEvent.setup();
    const { router } = renderApp('/devices');

    await screen.findByText('core-sw-01');

    await user.selectOptions(screen.getByLabelText('State'), 'Offline');

    await waitFor(() => {
      expect(router.state.location.search).toEqual(expect.objectContaining({ state: 'Offline' }));
    });
  });

  /** Filters compose: choosing a second one narrows the first rather than replacing it. */
  it('composes two filters rather than replacing one with the other', async () => {
    anEstate();

    const user = userEvent.setup();
    const { router } = renderApp('/devices?site=HQ');

    await screen.findByText('core-sw-01');

    await user.selectOptions(screen.getByLabelText('State'), 'Warning');

    await waitFor(() => {
      expect(router.state.location.search).toEqual(
        expect.objectContaining({ site: 'HQ', state: 'Warning' }),
      );
    });

    expect(await screen.findByText('ap-floor-2')).toBeVisible();
    expect(screen.queryByText('core-sw-01')).not.toBeInTheDocument();
  });

  it('drops a filter value the API would refuse rather than sending it', async () => {
    anEstate();

    renderApp('/devices?state=Melted&vendor=Acme');

    // Unrecognised is not a filter, so the whole estate is listed rather than a 400 rendered.
    expect(await screen.findByText('core-sw-01')).toBeVisible();
    expect(screen.getByText('edge-fw-01')).toBeVisible();
  });

  it('clears every filter at once', async () => {
    anEstate();

    const user = userEvent.setup();
    const { router } = renderApp('/devices?state=Offline&site=Branch');

    await screen.findByText('edge-fw-01');

    await user.click(screen.getByRole('button', { name: 'Clear filters' }));

    await waitFor(() => {
      expect(router.state.location.search).toEqual({});
    });
  });

  /** DESIGN.md §8: the situation, then the next step, with no apology. */
  it('says what to do when the inventory is empty', async () => {
    setInventory({ devices: [] });

    renderApp('/devices');

    expect(await screen.findByText('No devices yet.')).toBeVisible();
    expect(screen.getByText('Run discovery or add one manually.')).toBeVisible();
  });

  it('says something different when a filter is what emptied the list', async () => {
    anEstate();

    renderApp('/devices?site=Nowhere');

    expect(await screen.findByText('No devices match these filters.')).toBeVisible();
    expect(
      screen.getByText('Widen or clear the filters to see the rest of the inventory.'),
    ).toBeVisible();
  });

  /** DESIGN.md §8 and CONVENTIONS.md §6: what failed, and a retry. */
  it('says what failed and offers a retry when the list cannot be loaded', async () => {
    setInventory({ failDeviceList: true });

    renderApp('/devices');

    expect(await screen.findByText('The device list could not be loaded.')).toBeVisible();
    expect(screen.getByRole('button', { name: 'Try again' })).toBeVisible();
  });

  it('recovers when the retry succeeds', async () => {
    const state = setInventory({ failDeviceList: true, devices: [makeDevice()] });

    const user = userEvent.setup();

    renderApp('/devices');

    await screen.findByText('The device list could not be loaded.');

    state.failDeviceList = false;

    await user.click(screen.getByRole('button', { name: 'Try again' }));

    expect(await screen.findByText('core-sw-01')).toBeVisible();
  });

  it('opens a device from its row', async () => {
    anEstate();

    const user = userEvent.setup();
    const { router } = renderApp('/devices');

    await user.click(await screen.findByRole('link', { name: 'core-sw-01' }));

    await waitFor(() => {
      expect(router.state.location.pathname).toBe('/devices/device-1');
    });
  });

  /** DESIGN.md §6: a trailing row menu, and CONVENTIONS.md §6: an aria-label on an icon control. */
  it('offers a row menu naming the device it acts on', async () => {
    anEstate();

    const user = userEvent.setup();

    renderApp('/devices');

    await screen.findByText('core-sw-01');

    await user.click(screen.getByRole('button', { name: 'Actions for core-sw-01' }));

    const menu = screen.getByRole('menu');

    expect(within(menu).getByRole('menuitem', { name: 'Open device' })).toBeVisible();
    expect(within(menu).getByRole('menuitem', { name: 'Edit device' })).toBeVisible();
  });

  it('hides the add control from a session that cannot write', async () => {
    anEstate();
    signInReadOnly();

    renderApp('/devices');

    await screen.findByText('core-sw-01');

    expect(screen.queryByRole('link', { name: 'Add device' })).not.toBeInTheDocument();
  });

  it('shows the add control to a session that can', async () => {
    anEstate();

    renderApp('/devices');

    await screen.findByText('core-sw-01');

    expect(screen.getByRole('link', { name: 'Add device' })).toBeVisible();
  });

  /**
   * DESIGN.md §9.6 renders no unbounded list past a hundred rows, and WP-1.7 has to carry 500
   * devices. Only the rows in view are in the DOM; the scroller holds the full height.
   */
  it('renders far fewer rows than it holds', async () => {
    setInventory({
      devices: Array.from({ length: 500 }, (_, index) =>
        makeDevice({
          id: `device-${index.toString()}`,
          hostname: `switch-${index.toString().padStart(3, '0')}`,
          primaryIpAddress: `10.1.${Math.floor(index / 254).toString()}.${((index % 254) + 1).toString()}`,
        }),
      ),
    });

    renderApp('/devices');

    expect(await screen.findByText('500 devices in the inventory.')).toBeVisible();

    // The header row plus the window of body rows — nowhere near five hundred.
    expect(screen.getAllByRole('row').length).toBeLessThan(60);
    expect(screen.getByText('switch-000')).toBeVisible();
  });
});

/**
 * Signs the session in as a read-only user, which is what hides every write control.
 *
 * It replaces the user on the API already standing rather than calling `resetApi`, which would
 * also empty the inventory the test has just set up.
 */
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
