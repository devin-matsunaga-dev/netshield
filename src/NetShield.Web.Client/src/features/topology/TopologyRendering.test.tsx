import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';

import { setTopology } from '@/test/msw/handlers';
import { makeReferenceEstate } from '@/test/msw/topologyApi';
import { renderApp } from '@/test/renderApp';

/**
 * The mechanism behind WP-2.4's "500 nodes pan and zoom at 60fps", which is the part of that
 * criterion a test can honestly hold.
 *
 * A frame rate is a claim about a browser compositing on real hardware and cannot be measured in
 * jsdom — an assertion here that said "60fps" would be a number this file made up. What *can* be
 * pinned is the design the frame rate depends on: React Flow moves the canvas by transforming one
 * container, so a pan or a zoom is one CSS transform and not five hundred React renders. If a
 * later change made the tiles re-render on viewport movement, the frame rate would collapse and
 * these two tests are what would notice.
 *
 * The measured frame rate belongs to the manual verification checklist.
 */
describe('how the canvas moves', () => {
  it('zooms by transforming one container and touching no tile', async () => {
    setTopology(makeReferenceEstate());

    const user = userEvent.setup();
    const { container } = renderApp('/network');

    await screen.findByRole('button', { name: /^fw-01,/ });

    const viewport = container.querySelector<HTMLElement>('.react-flow__viewport');
    const before = {
      transform: viewport?.style.transform,
      tiles: [...container.querySelectorAll('.react-flow__node')].map((node) => node.outerHTML),
    };

    await user.click(screen.getByRole('button', { name: 'Zoom in' }));

    expect(viewport?.style.transform).not.toBe(before.transform);
    expect(
      [...container.querySelectorAll('.react-flow__node')].map((node) => node.outerHTML),
    ).toEqual(before.tiles);
  });

  it('keeps every tile in the DOM rather than virtualizing the canvas', async () => {
    setTopology(makeReferenceEstate());

    const { container } = renderApp('/network');

    await screen.findByRole('button', { name: /^fw-01,/ });

    // Deliberate, and the reason the keyboard can focus a tile that is currently off-screen:
    // `onlyRenderVisibleElements` would re-render the visible set on every frame of a pan, which
    // is the opposite of what the 60fps criterion wants at this scale.
    expect(container.querySelectorAll('.react-flow__node')).toHaveLength(5);
  });
});
