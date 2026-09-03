import { Outlet } from '@tanstack/react-router';

import { AppShell } from '@/components/layout/AppShell';

/** The chrome the whole route tree renders inside. */
export function RootLayout() {
  return (
    <AppShell>
      <Outlet />
    </AppShell>
  );
}
