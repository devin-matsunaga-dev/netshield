import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import type { ReactNode } from 'react';
import { describe, expect, it } from 'vitest';

import type { AuthenticatedUser } from '@/api/types';
import { RequirePermission } from '@/features/session/components/RequirePermission';
import { sessionKeys } from '@/features/session/api/sessionKeys';
import { readOnlyUser, testUser } from '@/test/msw/handlers';

/** Renders below a cache already holding a session, which is what the `_app` guard guarantees. */
function renderWithSession(user: AuthenticatedUser | undefined, children: ReactNode) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  if (user !== undefined) {
    queryClient.setQueryData(sessionKeys.current(), user);
  }

  return render(<QueryClientProvider client={queryClient}>{children}</QueryClientProvider>);
}

describe('a permission-gated control', () => {
  it('is drawn for a session that holds the permission', () => {
    renderWithSession(
      testUser,
      <RequirePermission permission="InventoryWrite">
        <button type="button">Add device</button>
      </RequirePermission>,
    );

    expect(screen.getByRole('button', { name: 'Add device' })).toBeVisible();
  });

  it('is absent for a session that does not', () => {
    renderWithSession(
      readOnlyUser,
      <RequirePermission permission="InventoryWrite">
        <button type="button">Add device</button>
      </RequirePermission>,
    );

    expect(screen.queryByRole('button', { name: 'Add device' })).not.toBeInTheDocument();
  });

  it('draws the fallback instead when one is given', () => {
    renderWithSession(
      readOnlyUser,
      <RequirePermission permission="InventoryWrite" fallback={<p>Read-only access</p>}>
        <button type="button">Add device</button>
      </RequirePermission>,
    );

    expect(screen.getByText('Read-only access')).toBeVisible();
  });

  it('holds nothing when there is no session at all', () => {
    // The moment between a session ending and the redirect landing. Failing closed costs
    // nothing: the API is what actually refuses.
    renderWithSession(
      undefined,
      <RequirePermission permission="InventoryRead">
        <p>Devices</p>
      </RequirePermission>,
    );

    expect(screen.queryByText('Devices')).not.toBeInTheDocument();
  });
});
