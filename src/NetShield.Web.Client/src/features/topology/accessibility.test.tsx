import { screen } from '@testing-library/react';
import { describe, it } from 'vitest';

import { expectNoAccessibilityViolations } from '@/test/axe';
import { setTopology } from '@/test/msw/handlers';
import { makeGraphNode, makeReferenceEstate } from '@/test/msw/topologyApi';
import { renderApp } from '@/test/renderApp';

/**
 * CONVENTIONS.md §6 asks for keyboard reach, a visible focus ring and a label on every icon-only
 * control; DESIGN.md §9.7 asks every chart for a text summary and a table fallback. These cover
 * the half a machine can see, on every state of the screen this package adds.
 */
describe('the topology screen', () => {
  it('has no accessibility violation on the map', async () => {
    setTopology(makeReferenceEstate());

    const { container } = renderApp('/network');
    await screen.findByRole('button', { name: /^fw-01,/ });

    await expectNoAccessibilityViolations(container);
  });

  it('has no accessibility violation on the map with an unknown device on it', async () => {
    const estate = makeReferenceEstate();

    setTopology({
      ...estate,
      nodes: [
        ...estate.nodes,
        makeGraphNode({ deviceId: 'x', hostname: 'sw-x', state: 'Unknown' }),
      ],
    });

    const { container } = renderApp('/network');
    await screen.findByRole('button', { name: /^sw-x,/ });

    await expectNoAccessibilityViolations(container);
  });

  it('has no accessibility violation on the table fallback', async () => {
    setTopology(makeReferenceEstate());

    const { container } = renderApp('/network?tab=table');
    await screen.findByRole('table', { name: 'Links on the map' });

    await expectNoAccessibilityViolations(container);
  });

  it('has no accessibility violation on the empty state', async () => {
    setTopology({});

    const { container } = renderApp('/network');
    await screen.findByText('No topology yet.');

    await expectNoAccessibilityViolations(container);
  });

  it('has no accessibility violation with an error showing', async () => {
    setTopology({ failGraph: true });

    const { container } = renderApp('/network');
    await screen.findByRole('alert');

    await expectNoAccessibilityViolations(container);
  });
});
