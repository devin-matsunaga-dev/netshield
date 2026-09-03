import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';

import { expectNoAccessibilityViolations } from '@/test/axe';
import { renderApp } from '@/test/renderApp';
import { setViewportWidth } from '@/test/viewport';

/** DESIGN.md §5: below 1024px the sidebar is an overlay drawer rather than a column. */
describe('the shell below 1024px', () => {
  it('hides the sidebar behind a control in the header', async () => {
    setViewportWidth(820);
    renderApp();

    expect(await screen.findByRole('button', { name: 'Open navigation' })).toBeVisible();
    expect(screen.queryByRole('navigation')).not.toBeInTheDocument();
  });

  it('opens the drawer, and closes it once a destination is chosen', async () => {
    const user = userEvent.setup();
    setViewportWidth(820);
    renderApp();

    await user.click(await screen.findByRole('button', { name: 'Open navigation' }));

    const devices = screen.getByRole('link', { name: 'Devices' });
    expect(devices).toBeVisible();

    await user.click(devices);

    await waitFor(() => {
      expect(screen.queryByRole('navigation')).not.toBeInTheDocument();
    });
    expect(await screen.findByRole('heading', { level: 1, name: 'Devices' })).toBeVisible();
  });

  it('closes the drawer from the backdrop', async () => {
    const user = userEvent.setup();
    setViewportWidth(820);
    renderApp();

    await user.click(await screen.findByRole('button', { name: 'Open navigation' }));
    await user.click(screen.getByRole('button', { name: 'Close navigation' }));

    expect(screen.queryByRole('navigation')).not.toBeInTheDocument();
  });

  it('offers no collapse control, because a drawer is open or it is not', async () => {
    const user = userEvent.setup();
    setViewportWidth(820);
    renderApp();

    await user.click(await screen.findByRole('button', { name: 'Open navigation' }));

    expect(screen.queryByRole('button', { name: 'Collapse sidebar' })).not.toBeInTheDocument();
  });

  it('becomes a column again when the window is widened', async () => {
    setViewportWidth(820);
    renderApp();

    await screen.findByRole('button', { name: 'Open navigation' });

    setViewportWidth(1536);

    await waitFor(() => {
      expect(screen.getByRole('navigation')).toBeVisible();
    });
    expect(screen.queryByRole('button', { name: 'Open navigation' })).not.toBeInTheDocument();
  });

  it('has no accessibility violations with the drawer open', async () => {
    const user = userEvent.setup();
    setViewportWidth(820);
    const { container } = renderApp();

    await user.click(await screen.findByRole('button', { name: 'Open navigation' }));

    await expectNoAccessibilityViolations(container);
  });
});
