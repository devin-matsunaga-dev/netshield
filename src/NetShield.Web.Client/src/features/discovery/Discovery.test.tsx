import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';

import { api, inventory, setInventory } from '@/test/msw/handlers';
import {
  makeCandidate,
  makeRun,
  makeRunDetail,
  makeSeed,
  type InventoryApiState,
} from '@/test/msw/inventoryApi';
import { renderApp } from '@/test/renderApp';

const runId = '019226b4-4000-7000-8000-000000000001';
const seedId = '019226b4-5000-7000-8000-000000000001';

function aDiscoveredEstate(overrides: Partial<InventoryApiState> = {}) {
  return setInventory({
    candidates: [
      makeCandidate({ id: 'candidate-1', address: '192.0.2.37', timesSeen: 3 }),
      makeCandidate({ id: 'candidate-2', address: '192.0.2.42', status: 'Ignored' }),
    ],
    runs: [makeRun({ id: runId })],
    runDetail: new Map([[runId, makeRunDetail({ id: runId })]]),
    runHosts: new Map([
      [
        runId,
        [
          {
            id: 'host-1',
            runId,
            address: '192.0.2.37',
            rttMilliseconds: 1.2,
            outcome: 'NewCandidate',
            candidateId: 'candidate-1',
            deviceId: null,
            observedAt: '2026-09-08T09:00:00.000Z',
          },
          {
            id: 'host-2',
            runId,
            address: '10.0.0.1',
            rttMilliseconds: 0.9,
            outcome: 'ExistingDevice',
            candidateId: null,
            deviceId: 'device-1',
            observedAt: '2026-09-08T09:00:00.000Z',
          },
        ],
      ],
    ]),
    seeds: [makeSeed({ id: seedId })],
    ignores: [
      {
        id: 'ignore-1',
        cidr: '192.0.2.0/24',
        reason: 'Printer VLAN',
        createdAt: '2026-09-08T09:00:00.000Z',
      },
    ],
    ...overrides,
  });
}

describe('the discovery screen', () => {
  /**
   * The half of SPEC.md §2's "results appear as reviewable candidates" that WP-1.6 could not
   * build: the candidates existed behind an endpoint and nobody could see them.
   */
  it('opens on the candidates awaiting review', async () => {
    aDiscoveredEstate();

    renderApp('/devices/discovery');

    expect(await screen.findByText('192.0.2.37')).toBeVisible();

    // Defaults to `New`, so a candidate somebody already dismissed is not in the way.
    expect(screen.queryByText('192.0.2.42')).not.toBeInTheDocument();
  });

  it('shows the settled candidates when asked for them', async () => {
    aDiscoveredEstate();

    const user = userEvent.setup();

    renderApp('/devices/discovery');

    await screen.findByText('192.0.2.37');

    await user.selectOptions(screen.getByLabelText('Status'), 'Ignored');

    expect(await screen.findByText('192.0.2.42')).toBeVisible();
  });

  it('says what a candidate is when there is nothing to review', async () => {
    setInventory({ candidates: [] });

    renderApp('/devices/discovery');

    expect(await screen.findByText('Nothing is waiting for review.')).toBeVisible();
  });

  it('promotes a candidate into a device', async () => {
    aDiscoveredEstate();

    const user = userEvent.setup();

    renderApp('/devices/discovery');

    await user.click(await screen.findByRole('button', { name: 'Promote' }));

    const dialog = await screen.findByRole('dialog', { name: 'Promote 192.0.2.37' });

    await user.type(within(dialog).getByLabelText('Hostname'), 'unknown-host-37');
    await user.click(within(dialog).getByRole('button', { name: 'Promote to device' }));

    await waitFor(() => {
      expect(writeTo('POST', '/discovery/candidates/candidate-1/promote')).toEqual(
        expect.objectContaining({ hostname: 'unknown-host-37' }),
      );
    });

    expect(await screen.findByText('Device added')).toBeVisible();
  });

  /**
   * A sweep established only that something answered. Offering a vendor or a credential here
   * would be inventing a fact, and assigning a credential would route around the
   * `CredentialsManage` boundary WP-1.2 drew.
   */
  it('offers no vendor and no credential when promoting', async () => {
    aDiscoveredEstate();

    const user = userEvent.setup();

    renderApp('/devices/discovery');

    await user.click(await screen.findByRole('button', { name: 'Promote' }));

    const dialog = await screen.findByRole('dialog', { name: 'Promote 192.0.2.37' });

    expect(within(dialog).queryByLabelText('Vendor')).not.toBeInTheDocument();
    expect(within(dialog).queryByLabelText(/credential/i)).not.toBeInTheDocument();
  });

  it('dismisses a candidate onto the ignore list', async () => {
    aDiscoveredEstate();

    const user = userEvent.setup();

    renderApp('/devices/discovery');

    await user.click(await screen.findByRole('button', { name: 'Ignore' }));

    await waitFor(() => {
      expect(
        inventory.writes.some((write) => write.path === '/discovery/candidates/candidate-1/ignore'),
      ).toBe(true);
    });
  });

  it('hides promote and ignore from a session that cannot write', async () => {
    aDiscoveredEstate();
    signInReadOnly();

    renderApp('/devices/discovery');

    await screen.findByText('192.0.2.37');

    expect(screen.queryByRole('button', { name: 'Promote' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Ignore' })).not.toBeInTheDocument();
  });

  describe('the runs tab', () => {
    it('puts what was in scope beside what answered', async () => {
      aDiscoveredEstate();

      renderApp('/devices/discovery?tab=runs');

      const table = await screen.findByRole('table', { name: 'Discovery runs' });

      expect(within(table).getByText('254')).toBeVisible();
      expect(within(table).getByText('3')).toBeVisible();
      expect(within(table).getByText('Campus core')).toBeVisible();
    });

    it('opens a run', async () => {
      aDiscoveredEstate();

      const user = userEvent.setup();
      const { router } = renderApp('/devices/discovery?tab=runs');

      await user.click(await screen.findByRole('row', { name: /Campus core/ }));

      await waitFor(() => {
        expect(router.state.location.pathname).toBe(`/devices/discovery/runs/${runId}`);
      });
    });
  });

  describe('a run', () => {
    it('shows what was swept and what answered', async () => {
      aDiscoveredEstate();

      renderApp(`/devices/discovery/runs/${runId}`);

      expect(await screen.findByText('10.0.0.0/24')).toBeVisible();
      expect(screen.getByText('3 of 254')).toBeVisible();
      expect(screen.getByText('1 of 1 completed')).toBeVisible();
    });

    it('lists the hosts that answered and what each turned out to be', async () => {
      aDiscoveredEstate();

      renderApp(`/devices/discovery/runs/${runId}`);

      const table = await screen.findByRole('table', { name: 'Hosts that answered' });

      expect(within(table).getByText('New candidate')).toBeVisible();
      expect(within(table).getByText('Already a device')).toBeVisible();
    });

    it('filters the hosts by outcome', async () => {
      aDiscoveredEstate();

      const user = userEvent.setup();

      renderApp(`/devices/discovery/runs/${runId}`);

      const table = await screen.findByRole('table', { name: 'Hosts that answered' });

      await user.selectOptions(screen.getByLabelText('Outcome'), 'ExistingDevice');

      // Scoped to the table: the outcome filter offers the same words as options.
      await waitFor(() => {
        expect(within(table).queryByText('New candidate')).not.toBeInTheDocument();
      });

      expect(within(table).getByText('Already a device')).toBeVisible();
    });

    /**
     * Three terminal statuses rather than two, because a run that swept nine spans and failed
     * the tenth found something real *and* missed a tenth of the range.
     */
    it('says when part of the range was never probed', async () => {
      aDiscoveredEstate({
        runDetail: new Map([
          [
            runId,
            makeRunDetail({
              id: runId,
              status: 'PartiallyFailed',
              jobCount: 10,
              jobsFailed: 1,
              jobsCompleted: 9,
            }),
          ],
        ]),
      });

      renderApp(`/devices/discovery/runs/${runId}`);

      expect(await screen.findByText(/sweep jobs/)).toBeVisible();
      expect(screen.getByText(/never probed/)).toBeVisible();
    });

    it('explains why silence is not a row', async () => {
      aDiscoveredEstate({ runHosts: new Map([[runId, []]]) });

      renderApp(`/devices/discovery/runs/${runId}`);

      expect(await screen.findByText('Nothing answered this sweep.')).toBeVisible();
      expect(screen.getByText(/Silence is not a row/)).toBeVisible();
    });
  });

  describe('the seeds tab', () => {
    it('lists the ranges NetShield sweeps', async () => {
      aDiscoveredEstate();

      renderApp('/devices/discovery?tab=seeds');

      const table = await screen.findByRole('table', { name: 'Discovery seeds' });

      expect(within(table).getByText('Campus core')).toBeVisible();
      expect(within(table).getByText('Every 1 day')).toBeVisible();
    });

    /**
     * A fresh installation with no seed can sweep nothing, so the seed form is here rather than
     * waiting for the Policies screen — a discovery screen that can run but cannot define a
     * target is a dead end.
     */
    it('says what to do when nothing has been seeded', async () => {
      setInventory({ seeds: [] });

      renderApp('/devices/discovery?tab=seeds');

      expect(await screen.findByText('No discovery seeds yet.')).toBeVisible();
      expect(screen.getByText(/Nothing is discovered until one exists/)).toBeVisible();
    });

    it('adds a seed', async () => {
      setInventory({ seeds: [] });

      const user = userEvent.setup();

      renderApp('/devices/discovery?tab=seeds');

      const [open] = await screen.findAllByRole('button', { name: 'Add seed' });

      if (open === undefined) {
        throw new Error('The seeds tab offers no way to add one.');
      }

      await user.click(open);

      const dialog = await screen.findByRole('dialog', { name: 'Add seed' });

      await user.type(within(dialog).getByLabelText('Name'), 'Branch office');
      await user.type(within(dialog).getByLabelText('Ranges'), '10.1.0.0/24\n10.2.0.0/24');
      await user.click(within(dialog).getByRole('button', { name: 'Add seed' }));

      await waitFor(() => {
        expect(writeTo('POST', '/discovery/seeds')).toEqual(
          expect.objectContaining({
            name: 'Branch office',
            ranges: ['10.1.0.0/24', '10.2.0.0/24'],
          }),
        );
      });
    });

    it('starts a run of a seed on demand', async () => {
      aDiscoveredEstate();

      const user = userEvent.setup();

      renderApp('/devices/discovery?tab=seeds');

      await user.click(await screen.findByRole('button', { name: 'Run now' }));

      await waitFor(() => {
        expect(
          inventory.writes.some(
            (write) => write.method === 'POST' && write.path === '/discovery/runs',
          ),
        ).toBe(true);
      });

      expect(await screen.findByText(/Sweeping 254 addresses/)).toBeVisible();
    });

    it('hides every seed control from a session that holds neither permission', async () => {
      aDiscoveredEstate();
      signInReadOnly();

      renderApp('/devices/discovery?tab=seeds');

      await screen.findByRole('table', { name: 'Discovery seeds' });

      expect(screen.queryByRole('button', { name: 'Add seed' })).not.toBeInTheDocument();
      expect(screen.queryByRole('button', { name: 'Run now' })).not.toBeInTheDocument();
      expect(screen.queryByRole('button', { name: 'Edit' })).not.toBeInTheDocument();
    });
  });

  describe('the ignore list', () => {
    it('lists what discovery will never offer again', async () => {
      aDiscoveredEstate();

      renderApp('/devices/discovery?tab=ignored');

      expect(await screen.findByText('192.0.2.0/24')).toBeVisible();
      expect(screen.getByText('Printer VLAN')).toBeVisible();
    });

    it('adds an address to it', async () => {
      aDiscoveredEstate();

      const user = userEvent.setup();

      renderApp('/devices/discovery?tab=ignored');

      await user.type(await screen.findByLabelText('Address or range'), '198.51.100.0/24');
      await user.type(screen.getByLabelText('Reason'), 'Lab bench');
      await user.click(screen.getByRole('button', { name: 'Add to ignore list' }));

      await waitFor(() => {
        expect(writeTo('POST', '/discovery/ignores')).toEqual({
          cidr: '198.51.100.0/24',
          reason: 'Lab bench',
        });
      });
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

/**
 * The write the SPA made to a given route, or nothing. Reading it back this way keeps the
 * matchers out of an object literal, where they would be `any` and the lint would say so.
 */
function writeTo(method: string, path: string): unknown {
  return inventory.writes.find((write) => write.method === method && write.path === path)?.body;
}
