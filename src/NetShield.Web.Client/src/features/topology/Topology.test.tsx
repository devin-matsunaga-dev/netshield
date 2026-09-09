import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';

import { setInventory, setTopology, topology } from '@/test/msw/handlers';
import { makeDetail } from '@/test/msw/inventoryApi';
import {
  makeGraphEdge,
  makeGraphNode,
  makeReferenceEstate,
  testLayout,
} from '@/test/msw/topologyApi';
import { renderApp } from '@/test/renderApp';

const firewallId = '019226b4-1000-7000-8000-000000000001';

/** A tile is a button whose accessible name starts with the hostname. */
function tile(hostname: string) {
  return screen.findByRole('button', { name: new RegExp(`^${hostname},`) });
}

describe('the topology map', () => {
  it('draws a tile for every device and a line for every link', async () => {
    setTopology(makeReferenceEstate());

    const { container } = renderApp('/network');

    await tile('fw-01');

    for (const hostname of ['core-sw-01', 'core-sw-02', 'acc-sw-01', 'acc-sw-02']) {
      expect(await tile(hostname)).toBeVisible();
    }

    expect(container.querySelectorAll('.react-flow__edge')).toHaveLength(5);
  });

  it('says what a tile is and what state it is in, not only what colour its border is', async () => {
    setTopology(makeReferenceEstate());

    renderApp('/network');

    // DESIGN.md §9.2: colour is never the only signal. The border is the state and the
    // accessible name says it in words.
    expect(await tile('acc-sw-02')).toHaveAccessibleName(
      'acc-sw-02, Switch at Floor 2. Offline. 1 link on the map.',
    );
  });

  it('says on the tile how many of a device links it is not drawing', async () => {
    setTopology(makeReferenceEstate());

    renderApp('/network');

    // WP-2.3: a node is a device, so an edge to something NetShield does not monitor is counted
    // rather than drawn. Without this the firewall shows two cables and reports three.
    const firewall = await tile('fw-01');

    expect(firewall).toHaveAccessibleName(
      'fw-01, Firewall at HQ. Online. 2 links on the map. 1 link to something NetShield does not monitor, which the map cannot draw.',
    );
    expect(within(firewall).getByText('Firewall · +1')).toBeVisible();
  });

  it('reads every cursor page before it draws, however many there are', async () => {
    // Three pages, not two. A two-page graph passes this whether or not the accumulation
    // actually continues, because `hasNextPage` turns false on the second page and any
    // dependency change would restart a stalled effect. The third page is where a graph that
    // stops accumulating stops silently — at 400 devices, under SPEC.md §1's target estate.
    const nodes = Array.from({ length: 450 }, (_, index) =>
      makeGraphNode({
        deviceId: `019226b4-1000-7000-8000-${index.toString().padStart(12, '0')}`,
        hostname: `sw-${index.toString()}`,
        rank: index,
        y: index * 176,
      }),
    );

    setTopology({
      nodes,
      components: [
        {
          index: 0,
          rootDeviceId: nodes[0]?.deviceId ?? '',
          nodeCount: 450,
          edgeCount: 0,
          depth: 449,
        },
      ],
      layout: testLayout,
    });

    const { container } = renderApp('/network');

    await waitFor(
      () => {
        expect(container.querySelectorAll('.react-flow__node')).toHaveLength(450);
      },
      { timeout: 20000 },
    );

    expect(topology.reads).toEqual([
      { cursor: null, limit: '200' },
      { cursor: '200', limit: '200' },
      { cursor: '400', limit: '200' },
    ]);
  }, 30000);

  it('opens the device behind a tile', async () => {
    setTopology(makeReferenceEstate());
    setInventory({
      detail: new Map([[firewallId, makeDetail({ id: firewallId, hostname: 'fw-01' })]]),
    });

    const user = userEvent.setup();

    renderApp('/network');
    await user.click(await tile('fw-01'));

    expect(await screen.findByRole('heading', { level: 1, name: 'fw-01' })).toBeVisible();
  });
});

describe('the topology legend', () => {
  it('carries the three states the reference card legends', async () => {
    setTopology(makeReferenceEstate());

    renderApp('/network');
    await tile('fw-01');

    const legend = screen.getByRole('list', { name: 'Device state' });

    expect(within(legend).getByText('Online')).toBeVisible();
    expect(within(legend).getByText('Warning')).toBeVisible();
    expect(within(legend).getByText('Offline')).toBeVisible();
    // Not in the reference, and no tile on this map is drawn in it.
    expect(within(legend).queryByText('Unknown')).not.toBeInTheDocument();
  });

  it('adds Unknown once a tile is actually drawn in it', async () => {
    const estate = makeReferenceEstate();

    setTopology({
      ...estate,
      nodes: [
        ...estate.nodes,
        makeGraphNode({ deviceId: 'x', hostname: 'sw-x', state: 'Unknown' }),
      ],
    });

    renderApp('/network');
    await tile('sw-x');

    expect(
      within(screen.getByRole('list', { name: 'Device state' })).getByText('Unknown'),
    ).toBeVisible();
  });
});

describe('the topology table fallback', () => {
  it('lists the same devices the map draws, in the graph ordering', async () => {
    setTopology(makeReferenceEstate());

    renderApp('/network?tab=table');

    const devices = await screen.findByRole('table', { name: 'Devices on the map' });

    for (const hostname of ['fw-01', 'core-sw-01', 'core-sw-02', 'acc-sw-01', 'acc-sw-02']) {
      expect(within(devices).getByRole('link', { name: hostname })).toBeVisible();
    }
  });

  it('says of a link what the drawn line deliberately does not', async () => {
    setTopology(makeReferenceEstate());

    renderApp('/network?tab=table');

    const links = await screen.findByRole('table', { name: 'Links on the map' });

    // The CDP-only, one-sided edge. On the canvas it is drawn exactly like the others.
    expect(within(links).getByText('Possible')).toBeVisible();
    expect(within(links).getByText('One end')).toBeVisible();
    expect(within(links).getByText('CDP')).toBeVisible();
  });

  it('is reachable from the map and puts the view in the address', async () => {
    setTopology(makeReferenceEstate());

    const user = userEvent.setup();
    const { router } = renderApp('/network');

    await tile('fw-01');
    await user.click(screen.getByRole('tab', { name: 'Table' }));

    expect(await screen.findByRole('table', { name: 'Devices on the map' })).toBeVisible();
    await waitFor(() => {
      expect(router.state.location.searchStr).toContain('tab=table');
    });
  });

  it('falls back to the map when the address names a view that does not exist', async () => {
    setTopology(makeReferenceEstate());

    renderApp('/network?tab=elsewhere');

    expect(await tile('fw-01')).toBeVisible();
  });
});

describe('the topology screen', () => {
  it('says what to do when nothing has built a graph yet', async () => {
    setTopology({});

    renderApp('/network');

    expect(await screen.findByText('No topology yet.')).toBeVisible();
    expect(screen.getByText(/Wait for the topology schedule to reach a device/)).toBeVisible();
  });

  it('says what failed and offers a retry', async () => {
    setTopology({ failGraph: true });

    renderApp('/network');

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'The topology map could not be loaded.',
    );
    expect(screen.getByRole('button', { name: 'Try again' })).toBeVisible();
  });

  it('says so when the estate is larger than one map can hold', async () => {
    const estate = makeReferenceEstate();

    setTopology({ ...estate, truncated: true });

    renderApp('/network');
    await tile('fw-01');

    expect(screen.getByRole('status')).toHaveTextContent(
      'The estate is larger than one map can hold. This map shows 5 of 5 devices.',
    );
  });

  it('counts the graph in the page subtitle', async () => {
    setTopology(makeReferenceEstate());

    renderApp('/network');
    await tile('fw-01');

    expect(
      screen.getByText('5 devices and 5 links on the map, from neighbour, ARP and routing data.'),
    ).toBeVisible();
  });

  it('summarises the map for a reader who cannot see it', async () => {
    const estate = makeReferenceEstate();

    setTopology({
      ...estate,
      edges: [
        ...estate.edges,
        // An edge whose far end the graph never delivered, which only truncation produces.
        makeGraphEdge({ id: 'cut', aDeviceId: estate.nodes[0]?.deviceId ?? '', bDeviceId: 'gone' }),
      ],
      truncated: true,
    });

    renderApp('/network');
    await tile('fw-01');

    const canvas = screen.getByRole('group', { name: 'Topology map' });
    const summary = document.getElementById(canvas.getAttribute('aria-describedby') ?? '');

    expect(summary?.textContent).toBe(
      '5 devices and 5 links, in 1 connected group. 3 online, 1 warning, 1 offline. ' +
        '1 link leads to something NetShield does not monitor and is not drawn. ' +
        'The estate is larger than one map can hold, so the map is cut short. ' +
        '1 link could not be drawn because the far device is not on the map.',
    );
  });
});
