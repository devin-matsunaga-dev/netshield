import { createFileRoute } from '@tanstack/react-router';

import { NotFoundPage } from '@/components/layout/NotFoundPage';

/**
 * Any address the route tree does not claim, inside the shell — so a mistyped URL still leaves
 * the reader a sidebar to navigate out with, rather than a bare page.
 *
 * It sits under `_app` and so behind the guard: an unauthenticated visitor to a path that does
 * not exist is sent to sign in like any other, and learns nothing about which paths do.
 */
export const Route = createFileRoute('/_app/$')({
  component: NotFoundPage,
});
