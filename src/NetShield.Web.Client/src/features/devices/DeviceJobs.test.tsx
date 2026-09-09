import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';

import { inventory, readOnlyUser, resetApi, setInventory } from '@/test/msw/handlers';
import { makeDetail, makeDevice, makeJob } from '@/test/msw/inventoryApi';
import { renderApp } from '@/test/renderApp';

const deviceId = '019226b4-1000-7000-8000-000000000001';

function anEstate(jobs: ReturnType<typeof makeJob>[] = [makeJob()]) {
  return setInventory({
    devices: [makeDevice({ id: deviceId })],
    detail: new Map([[deviceId, makeDetail({ id: deviceId })]]),
    jobs: new Map([[deviceId, jobs]]),
  });
}

const jobsTab = `/devices/${deviceId}?tab=jobs`;

describe('the device job queue', () => {
  it('shows what NetShield has asked of the device', async () => {
    anEstate([
      makeJob({ id: 'job-1', walk: 'neighbors', status: 'Pending' }),
      makeJob({
        id: 'job-2',
        kind: 'Poll',
        walk: null,
        status: 'Succeeded',
        cancellable: false,
        leasedBy: 'collector-dev',
        attempts: 1,
      }),
    ]);

    renderApp(jobsTab);

    const table = await screen.findByRole('table', { name: 'Collector jobs' });

    // The walk discriminator, not the bare kind: five different reads share `Discover`.
    expect(within(table).getByText('Neighbours')).toBeVisible();
    expect(within(table).getByText('Queued')).toBeVisible();

    // A kind with no walks reads as itself.
    expect(within(table).getByText('Reachability poll')).toBeVisible();
    expect(within(table).getByText('collector-dev')).toBeVisible();
  });

  it('says so when nothing has been asked of the device', async () => {
    anEstate([]);

    renderApp(jobsTab);

    expect(await screen.findByText('Nothing has been asked of this device yet.')).toBeVisible();
    expect(screen.getByText(/Run a walk above/)).toBeVisible();
  });

  it('says what failed and offers a retry', async () => {
    anEstate();
    setInventory({
      devices: [makeDevice({ id: deviceId })],
      detail: new Map([[deviceId, makeDetail({ id: deviceId })]]),
      failJobList: true,
    });

    renderApp(jobsTab);

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'The job queue could not be loaded.',
    );
  });

  it('narrows the queue to one status', async () => {
    anEstate([
      makeJob({ id: 'job-1', walk: 'neighbors', status: 'Pending' }),
      makeJob({ id: 'job-2', walk: 'routes', status: 'Failed', cancellable: false }),
    ]);

    const user = userEvent.setup();

    renderApp(jobsTab);
    await screen.findByRole('table', { name: 'Collector jobs' });

    await user.selectOptions(screen.getByLabelText('Status'), 'Failed');

    // Scoped to the table: the status filter's own options carry these words too.
    await waitFor(() => {
      const table = screen.getByRole('table', { name: 'Collector jobs' });

      expect(within(table).queryByText('Neighbours')).not.toBeInTheDocument();
      expect(within(table).getByText('Routes')).toBeVisible();
    });
  });
});

describe('cancelling a queued job', () => {
  it('takes it out of the queue and leaves the row behind', async () => {
    anEstate([makeJob({ id: 'job-1', walk: 'neighbors', status: 'Pending' })]);

    const user = userEvent.setup();

    renderApp(jobsTab);

    const table = await screen.findByRole('table', { name: 'Collector jobs' });

    await user.click(within(table).getByRole('button', { name: 'Cancel Neighbours' }));

    // Cancelled, not deleted — the row stays and says what happened to it.
    await waitFor(() => {
      expect(within(table).getByText('Cancelled')).toBeVisible();
    });

    expect(within(table).getByText('Neighbours')).toBeVisible();

    expect(inventory.writes).toContainEqual({
      method: 'POST',
      path: `/devices/${deviceId}/jobs/job-1/cancel`,
      body: null,
    });
  });

  it('offers no cancel on a job the server says is past cancelling', async () => {
    anEstate([makeJob({ id: 'job-1', walk: 'routes', status: 'Leased', cancellable: false })]);

    renderApp(jobsTab);

    const table = await screen.findByRole('table', { name: 'Collector jobs' });

    // `cancellable` is the server's answer rather than the client's guess, so the control is
    // never offered where the API would refuse it.
    expect(within(table).queryByRole('button', { name: /^Cancel/ })).not.toBeInTheDocument();
    expect(within(table).getByText('Running')).toBeVisible();
  });

  it('says so when a collector claimed the job first', async () => {
    anEstate([makeJob({ id: 'gone', walk: 'neighbors', status: 'Pending' })]);

    const user = userEvent.setup();

    renderApp(jobsTab);

    const table = await screen.findByRole('table', { name: 'Collector jobs' });
    const button = within(table).getByRole('button', { name: 'Cancel Neighbours' });

    // Claimed after the row rendered and before the button was pressed, which is ordinary on a
    // screen that refetches every five seconds.
    inventory.jobs.set(deviceId, [
      makeJob({ id: 'gone', walk: 'neighbors', status: 'Leased', cancellable: false }),
    ]);

    await user.click(button);

    expect(await screen.findByText('A collector has already started it.')).toBeVisible();
  });
});

describe('running a walk from the device screen', () => {
  it('queues the walk the button names', async () => {
    anEstate([]);

    const user = userEvent.setup();

    renderApp(jobsTab);

    await user.click(await screen.findByRole('button', { name: 'Neighbours' }));

    expect(await screen.findByText('Neighbours walk queued.')).toBeVisible();
    expect(inventory.writes).toContainEqual({
      method: 'POST',
      path: `/devices/${deviceId}/neighbor-walk`,
      body: null,
    });
  });

  it('offers every walk the API has a route for', async () => {
    anEstate([]);

    renderApp(jobsTab);

    for (const label of ['Fingerprint', 'Neighbours', 'Routes', 'VLANs', 'Clients']) {
      expect(await screen.findByRole('button', { name: label })).toBeVisible();
    }
  });

  it('explains the refusal when a walk is already queued', async () => {
    anEstate([]);
    inventory.walkOutstanding = true;

    const user = userEvent.setup();

    renderApp(jobsTab);

    await user.click(await screen.findByRole('button', { name: 'Routes' }));

    // The 409 is a rule rather than a fault, and the queue below is where it can be cleared.
    expect(await screen.findByText(/already queued/)).toBeVisible();
  });
});

describe('the job queue and permissions', () => {
  it('hides every control from a session that cannot run a walk', async () => {
    // `resetApi` empties the inventory, so the session is replaced before the estate is set.
    resetApi({ user: readOnlyUser });
    anEstate([makeJob({ id: 'job-1', walk: 'neighbors', status: 'Pending' })]);

    renderApp(jobsTab);

    await screen.findByRole('table', { name: 'Collector jobs' });

    // Hiding is presentation and never the boundary — the API refuses either way.
    expect(screen.queryByRole('button', { name: 'Neighbours' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^Cancel/ })).not.toBeInTheDocument();

    // The queue itself is InventoryRead, so it is still readable.
    const table = screen.getByRole('table', { name: 'Collector jobs' });

    expect(within(table).getByText('Neighbours')).toBeVisible();
  });
});
