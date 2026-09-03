import { screen, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';

import { navigation, navigationDestinations } from '@/app/navigation';
import { renderApp } from '@/test/renderApp';

describe('the route tree', () => {
  it.each([...navigationDestinations])('renders a page at %s', async (path) => {
    renderApp(path);

    await waitFor(() => {
      expect(screen.getByRole('heading', { level: 1 })).toBeInTheDocument();
    });

    expect(screen.getByRole('heading', { level: 1 }).textContent).not.toBe('Page not found');
  });

  it('sends the root to the overview', async () => {
    renderApp('/');

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Network overview' }),
    ).toBeVisible();
  });

  it.each([
    ['/security', 'Security posture'],
    ['/reports', 'Inventory report'],
    ['/administration', 'Users'],
  ])('sends %s to its first child, %s', async (section, heading) => {
    renderApp(section);

    expect(await screen.findByRole('heading', { level: 1, name: heading })).toBeVisible();
  });

  it('tells the reader what happened when the address matches nothing', async () => {
    renderApp('/no-such-screen');

    expect(await screen.findByRole('heading', { level: 1, name: 'Page not found' })).toBeVisible();
  });

  it('has a destination for every sidebar entry', () => {
    const entriesWithNoDestination = navigation.filter(
      (entry) => entry.to === undefined && (entry.children?.length ?? 0) === 0,
    );

    expect(entriesWithNoDestination).toEqual([]);
  });
});
