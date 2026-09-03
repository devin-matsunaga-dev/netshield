import { useSyncExternalStore } from 'react';

/**
 * Whether a CSS media query currently matches, kept in step with the browser.
 *
 * The shell needs this because the layout below 1024px is a different arrangement rather than a
 * differently-sized one (DESIGN.md §5): the sidebar becomes an overlay drawer, which is a
 * decision about markup and focus, not one CSS can make on its own.
 */
export function useMediaQuery(query: string): boolean {
  return useSyncExternalStore(
    (onChange) => {
      const list = window.matchMedia(query);

      list.addEventListener('change', onChange);

      return () => {
        list.removeEventListener('change', onChange);
      };
    },
    () => window.matchMedia(query).matches,
    // Nothing renders on a server; the value is only ever read in a browser.
    () => false,
  );
}
