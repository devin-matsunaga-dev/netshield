import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';

import { setClients } from '@/test/msw/handlers';
import { makeClient, printerId, laptopId } from '@/test/msw/clientsApi';
import { renderApp } from '@/test/renderApp';

/** A few endpoints, differing only in what a given test filters on. */
function anEstate() {
  return setClients({
    clients: [
      makeClient({
        id: laptopId,
        macAddress: 'AA:BB:CC:00:00:21',
        ipAddress: '10.10.0.21',
        vlanId: 10,
        hostname: 'laptop-7',
      }),
      makeClient({
        id: printerId,
        macAddress: 'AE:BB:CC:00:00:42',
        oui: 'AE:BB:CC',
        locallyAdministered: false,
        ipAddress: '10.20.0.42',
        vlanId: 20,
        ifIndex: 24,
        hostname: null,
      }),
      makeClient({
        id: '019226b4-2000-7000-8000-000000000003',
        macAddress: '11:22:33:00:00:99',
        oui: '11:22:33',
        locallyAdministered: false,
        // Seen in a forwarding database and never in an ARP table: a real and ordinary state,
        // and the reason the "holding an address" filter exists at all.
        ipAddress: null,
        vlanId: 30,
        hostname: null,
      }),
    ],
  });
}

describe('the client list', () => {
  it('shows every client with its address and where it is attached', async () => {
    anEstate();

    renderApp('/clients');

    expect(await screen.findByText('AA:BB:CC:00:00:21')).toBeVisible();
    expect(screen.getByText('10.10.0.21')).toBeVisible();
    expect(screen.getByText('AE:BB:CC:00:00:42')).toBeVisible();
    expect(screen.getAllByText('core-sw-01').length).toBeGreaterThan(0);
  });

  it('says how many clients there are, in the singular for one', async () => {
    setClients({ clients: [makeClient()] });

    renderApp('/clients');

    // The contract types every number as `number | string`, so an uncoerced comparison would
    // make this read "1 clients".
    expect(await screen.findByText('1 client seen on the network.')).toBeVisible();
  });

  it('names a locally administered address rather than showing a prefix that stands for nothing', async () => {
    // The IEEE registry is not in this repository, so an OUI is shown as itself — except when
    // nobody registered it, which is a different fact and worth saying.
    setClients({ clients: [makeClient({ locallyAdministered: true })] });

    renderApp('/clients');

    expect(await screen.findByText('Locally administered')).toBeVisible();
  });

  it('narrows by a MAC address however it is spelled', async () => {
    anEstate();

    renderApp('/clients');

    await screen.findByText('AA:BB:CC:00:00:21');

    await userEvent.type(screen.getByLabelText('Search'), 'aabb.cc00.0021');

    await waitFor(() => {
      expect(screen.queryByText('AE:BB:CC:00:00:42')).not.toBeInTheDocument();
    });

    expect(screen.getByText('AA:BB:CC:00:00:21')).toBeVisible();
  });

  it('narrows by VLAN', async () => {
    anEstate();

    renderApp('/clients');

    await screen.findByText('AA:BB:CC:00:00:21');

    await userEvent.type(screen.getByLabelText('VLAN'), '20');

    await waitFor(() => {
      expect(screen.queryByText('AA:BB:CC:00:00:21')).not.toBeInTheDocument();
    });

    expect(screen.getByText('AE:BB:CC:00:00:42')).toBeVisible();
  });

  it('narrows to the clients that hold an address', async () => {
    anEstate();

    renderApp('/clients');

    await screen.findByText('11:22:33:00:00:99');

    await userEvent.click(screen.getByLabelText('Holding an address'));

    await waitFor(() => {
      expect(screen.queryByText('11:22:33:00:00:99')).not.toBeInTheDocument();
    });

    expect(screen.getByText('AA:BB:CC:00:00:21')).toBeVisible();
  });

  it('keeps the filters in the address so they survive a refresh', async () => {
    anEstate();

    const { router } = renderApp('/clients');

    await screen.findByText('AA:BB:CC:00:00:21');

    await userEvent.type(screen.getByLabelText('VLAN'), '20');

    await waitFor(() => {
      expect(router.state.location.search).toMatchObject({ vlanId: 20 });
    });
  });

  it('reads the filters back out of the address', async () => {
    anEstate();

    renderApp('/clients?vlanId=20');

    expect(await screen.findByText('AE:BB:CC:00:00:42')).toBeVisible();
    expect(screen.queryByText('AA:BB:CC:00:00:21')).not.toBeInTheDocument();
  });

  it('drops a filter value it does not recognise rather than sending it', async () => {
    // The second parse, at the point of use. A TanStack Router route inherits its parent's
    // search parameters and merges its own over them, so a value this route rejected survives as
    // the root parsed it — and without the second pass it reaches the API as a 400.
    anEstate();

    renderApp('/clients?vlanId=melted');

    expect(await screen.findByText('AA:BB:CC:00:00:21')).toBeVisible();
    expect(screen.getByText('AE:BB:CC:00:00:42')).toBeVisible();
  });

  it('offers to clear the filters when they match nothing', async () => {
    anEstate();

    renderApp('/clients?vlanId=4000');

    expect(await screen.findByText('No clients match these filters.')).toBeVisible();

    // Two of them: the filter bar's, and the empty state's own offer. Both do the same thing,
    // and a reader who has reached the empty state is looking at the second.
    const [, inEmptyState] = screen.getAllByRole('button', { name: 'Clear filters' });

    if (inEmptyState === undefined) {
      throw new Error('The empty state should offer to clear the filters.');
    }

    await userEvent.click(inEmptyState);

    expect(await screen.findByText('AA:BB:CC:00:00:21')).toBeVisible();
  });

  it('says what to do when nothing has ever been seen', async () => {
    setClients();

    renderApp('/clients');

    expect(await screen.findByText('No clients yet.')).toBeVisible();
    expect(screen.getByText(/Add a device with an SNMP credential profile/)).toBeVisible();
  });

  it('offers a retry when the list cannot be loaded', async () => {
    setClients({ failClientList: true });

    renderApp('/clients');

    expect(await screen.findByText('The client list could not be loaded.')).toBeVisible();
    expect(screen.getByRole('button', { name: 'Try again' })).toBeVisible();
  });

  it('opens a client from its MAC address', async () => {
    anEstate();

    const { router } = renderApp('/clients');

    await userEvent.click(await screen.findByText('AA:BB:CC:00:00:21'));

    await waitFor(() => {
      expect(router.state.location.pathname).toBe(`/clients/${laptopId}`);
    });
  });
});
