import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, it } from 'vitest';

import { expectNoAccessibilityViolations } from '@/test/axe';
import {
  laptopId,
  makeClient,
  makeClientDetail,
  makeIpBinding,
  makePortBinding,
  makeResolution,
} from '@/test/msw/clientsApi';
import { setClients } from '@/test/msw/handlers';
import { renderApp } from '@/test/renderApp';

function anEstate() {
  return setClients({
    clients: [makeClient({ id: laptopId })],
    detail: new Map([[laptopId, makeClientDetail()]]),
    ipHistory: new Map([[laptopId, [makeIpBinding()]]]),
    portHistory: new Map([[laptopId, [makePortBinding()]]]),
    resolutions: new Map([['10.10.0.21', makeResolution()]]),
  });
}

/**
 * `axe` over every screen this package draws.
 *
 * The virtualized grid is where this earns its place: the rows are absolutely positioned, so
 * there is no real table for a screen reader to read and the ARIA roles are the whole of what
 * makes the columns nameable. A missing `columnheader` is invisible to a pair of eyes and total
 * to anything else.
 */
describe('the client screens', () => {
  it('the list has no accessibility violations', async () => {
    anEstate();

    const { container } = renderApp('/clients');

    await screen.findByText('AA:BB:CC:00:00:21');

    await expectNoAccessibilityViolations(container);
  });

  it('the list with an answered resolution has none', async () => {
    anEstate();

    const { container } = renderApp('/clients');

    await screen.findByText('Resolve an address');

    await userEvent.type(screen.getByLabelText('IP address'), '10.10.0.21');
    await userEvent.click(screen.getByRole('button', { name: 'Resolve' }));

    await screen.findByText('Hardware address');

    await expectNoAccessibilityViolations(container);
  });

  it('the empty list has none', async () => {
    setClients();

    const { container } = renderApp('/clients');

    await screen.findByText('No clients yet.');

    await expectNoAccessibilityViolations(container);
  });

  it('the error state has none', async () => {
    setClients({ failClientList: true });

    const { container } = renderApp('/clients');

    await screen.findByText('The client list could not be loaded.');

    await expectNoAccessibilityViolations(container);
  });

  it('the client overview has none', async () => {
    anEstate();

    const { container } = renderApp(`/clients/${laptopId}`);

    await screen.findByText('Identity');

    await expectNoAccessibilityViolations(container);
  });

  it('the address history has none', async () => {
    anEstate();

    const { container } = renderApp(`/clients/${laptopId}?tab=addresses`);

    await screen.findByRole('table', { name: /Every address this client has held/ });

    await expectNoAccessibilityViolations(container);
  });

  it('the port history has none', async () => {
    anEstate();

    const { container } = renderApp(`/clients/${laptopId}?tab=ports`);

    await screen.findByRole('table', { name: /Every port that has reported this client/ });

    await expectNoAccessibilityViolations(container);
  });
});
