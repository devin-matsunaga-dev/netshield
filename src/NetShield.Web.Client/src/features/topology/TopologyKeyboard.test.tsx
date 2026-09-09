import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';

import { setInventory, setTopology } from '@/test/msw/handlers';
import { makeDetail } from '@/test/msw/inventoryApi';
import { makeReferenceEstate } from '@/test/msw/topologyApi';
import { renderApp } from '@/test/renderApp';

/** The estate's tiles, in the graph ordering the canvas walks them in. */
const hostnames = ['fw-01', 'core-sw-01', 'core-sw-02', 'acc-sw-01', 'acc-sw-02'];

const firewallId = '019226b4-1000-7000-8000-000000000001';

async function tiles(): Promise<HTMLElement[]> {
  const found: HTMLElement[] = [];

  for (const hostname of hostnames) {
    found.push(await screen.findByRole('button', { name: new RegExp(`^${hostname},`) }));
  }

  return found;
}

/**
 * "The canvas is keyboard navigable" is half of a WP-2.4 criterion, and the half a machine can
 * check. The other half — that it is navigable in a way a person can stand — is the manual
 * checklist's.
 */
describe('walking the topology map with a keyboard', () => {
  it('puts exactly one tile in the tab order, not five hundred', async () => {
    setTopology(makeReferenceEstate());

    renderApp('/network');

    const found = await tiles();

    expect(found.filter((element) => element.tabIndex === 0)).toHaveLength(1);
    expect(found[0]?.tabIndex).toBe(0);
  });

  it('moves along the graph ordering with the arrow keys', async () => {
    setTopology(makeReferenceEstate());

    const user = userEvent.setup();

    renderApp('/network');
    const found = await tiles();

    found[0]?.focus();

    await user.keyboard('{ArrowDown}');
    expect(found[1]).toHaveFocus();

    await user.keyboard('{ArrowRight}');
    expect(found[2]).toHaveFocus();

    await user.keyboard('{ArrowLeft}');
    expect(found[1]).toHaveFocus();

    await user.keyboard('{ArrowUp}');
    expect(found[0]).toHaveFocus();
  });

  it('stops at the ends rather than wrapping round', async () => {
    setTopology(makeReferenceEstate());

    const user = userEvent.setup();

    renderApp('/network');
    const found = await tiles();

    found[0]?.focus();
    await user.keyboard('{ArrowUp}');

    // A map is a place rather than a list: running off the top and reappearing at the bottom of
    // an island the reader has never seen is disorientation, not navigation.
    expect(found[0]).toHaveFocus();
  });

  it('jumps to the first and last device with Home and End', async () => {
    setTopology(makeReferenceEstate());

    const user = userEvent.setup();

    renderApp('/network');
    const found = await tiles();

    found[0]?.focus();

    await user.keyboard('{End}');
    expect(found[4]).toHaveFocus();

    await user.keyboard('{Home}');
    expect(found[0]).toHaveFocus();
  });

  it('moves the roving tab stop with the focus', async () => {
    setTopology(makeReferenceEstate());

    const user = userEvent.setup();

    renderApp('/network');
    const found = await tiles();

    found[0]?.focus();
    await user.keyboard('{ArrowDown}');

    expect(found[0]?.tabIndex).toBe(-1);
    expect(found[1]?.tabIndex).toBe(0);
  });

  it('opens the device under the keyboard with Enter', async () => {
    setTopology(makeReferenceEstate());
    setInventory({
      detail: new Map([[firewallId, makeDetail({ id: firewallId, hostname: 'fw-01' })]]),
    });

    const user = userEvent.setup();

    renderApp('/network');
    const found = await tiles();

    found[0]?.focus();
    await user.keyboard('{Enter}');

    expect(await screen.findByRole('heading', { level: 1, name: 'fw-01' })).toBeVisible();
  });

  it('leaves a key it does not claim to the page', async () => {
    setTopology(makeReferenceEstate());

    const user = userEvent.setup();

    renderApp('/network');
    const found = await tiles();

    found[0]?.focus();
    await user.keyboard('{Tab}');

    // Tab is the browser's, not the canvas's: the roving index owns the arrows and nothing else.
    expect(found[0]).not.toHaveFocus();
  });
});
