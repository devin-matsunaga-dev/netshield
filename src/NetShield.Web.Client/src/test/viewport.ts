import { vi } from 'vitest';

/** jsdom's own default, and the width the docked layout starts at. */
export const defaultViewportWidth = 1024;

const listeners = new Set<() => void>();

/**
 * A `matchMedia` that answers from the window's width, which jsdom does not implement at all.
 *
 * The shell reads a media query to decide whether the sidebar is a column or a drawer
 * (DESIGN.md §5), so a stub that always answered "no" would silently test only the drawer.
 */
export function installMatchMedia(): void {
  listeners.clear();
  window.innerWidth = defaultViewportWidth;

  vi.stubGlobal(
    'matchMedia',
    vi.fn((query: string) => {
      const listen = (_: string, handler: () => void) => listeners.add(handler);
      const unlisten = (_: string, handler: () => void) => listeners.delete(handler);

      return {
        get matches() {
          return matches(query);
        },
        media: query,
        onchange: null,
        addEventListener: listen,
        removeEventListener: unlisten,
        addListener: listen,
        removeListener: unlisten,
        dispatchEvent: () => true,
      };
    }),
  );
}

/** Resizes the window and tells everything watching a media query about it. */
export function setViewportWidth(width: number): void {
  window.innerWidth = width;

  for (const notify of listeners) {
    notify();
  }
}

function matches(query: string): boolean {
  const minimum = /min-width:\s*(\d+)px/.exec(query);
  const maximum = /max-width:\s*(\d+)px/.exec(query);

  if (minimum?.[1] !== undefined) {
    return window.innerWidth >= Number(minimum[1]);
  }

  if (maximum?.[1] !== undefined) {
    return window.innerWidth <= Number(maximum[1]);
  }

  return false;
}
