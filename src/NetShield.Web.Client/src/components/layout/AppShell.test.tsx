import { readFileSync } from 'node:fs';

import { screen, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';

import { expectNoAccessibilityViolations } from '@/test/axe';
import { renderApp } from '@/test/renderApp';

describe('the application shell', () => {
  it('has no accessibility violations on a destination', async () => {
    const { container } = renderApp('/devices');

    await waitFor(() => {
      expect(screen.getByRole('heading', { level: 1 })).toBeInTheDocument();
    });

    await expectNoAccessibilityViolations(container);
  });

  it('has no accessibility violations with a section open', async () => {
    const { container } = renderApp('/administration/audit-log');

    await waitFor(() => {
      expect(screen.getByRole('link', { name: 'Audit log' })).toBeInTheDocument();
    });

    await expectNoAccessibilityViolations(container);
  });

  it('gives the navigation and the content each their own landmark', async () => {
    renderApp();

    expect(await screen.findByRole('navigation')).toBeInTheDocument();
    expect(screen.getByRole('main')).toBeInTheDocument();
    expect(screen.getByRole('banner')).toBeInTheDocument();
  });

  it('draws a focus ring on whatever the keyboard is on', () => {
    // Set once in the stylesheet rather than per control, so a control added later cannot be
    // added without one (CONVENTIONS.md §6).
    expect(readFileSync('src/styles/theme.css', 'utf8')).toContain(':focus-visible');
  });
});
