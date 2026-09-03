import { createRootRoute } from '@tanstack/react-router';

import { NotFoundPage } from '@/components/layout/NotFoundPage';
import { RootLayout } from '@/components/layout/RootLayout';

/**
 * The layout every route renders inside. The route tree below mirrors the sidebar in
 * `docs/design/reference-dashboard.png` (ARCHITECTURE.md §9).
 */
export const Route = createRootRoute({
  component: RootLayout,
  notFoundComponent: NotFoundPage,
});
