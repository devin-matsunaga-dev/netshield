import { vi } from 'vitest';

/** The scroll viewport a virtualized table gets in a test. Tall enough for a screenful of rows. */
export const testViewportHeight = 600;

/** And wide enough that nothing wraps in a way a test would trip over. */
export const testViewportWidth = 1280;

/**
 * Gives jsdom a layout, so that a virtualized list has a viewport to compute a window of rows
 * from.
 *
 * jsdom performs no layout at all: every element measures zero by zero, and `ResizeObserver`
 * does not exist. A virtualizer asked how tall its scroll container is therefore hears "nothing"
 * and renders no rows — the table would be empty in every test while working perfectly in a
 * browser, which is the worst of both.
 *
 * This is a test harness concern rather than a component one. Nothing in the application knows
 * it exists, and the alternative — a component that takes its own height as a prop so a test can
 * supply one — would be production code shaped by the test environment.
 */
export function installLayout(): void {
  vi.stubGlobal(
    'ResizeObserver',
    class {
      public observe(): void {
        // Nothing resizes in jsdom; the initial measurement below is the only one there is.
      }

      public unobserve(): void {
        // Same.
      }

      public disconnect(): void {
        // Same.
      }
    },
  );

  // A size for every element. `@tanstack/virtual-core` measures its scroll container with
  // `offsetWidth`/`offsetHeight`, which jsdom hard-codes to zero, so those are the two that
  // decide whether any row is rendered at all. `getBoundingClientRect` is given the same size
  // for anything else that asks.
  Object.defineProperty(HTMLElement.prototype, 'offsetWidth', {
    configurable: true,
    get: () => testViewportWidth,
  });

  Object.defineProperty(HTMLElement.prototype, 'offsetHeight', {
    configurable: true,
    get: () => testViewportHeight,
  });

  Element.prototype.getBoundingClientRect = function getBoundingClientRect(): DOMRect {
    return {
      width: testViewportWidth,
      height: testViewportHeight,
      top: 0,
      left: 0,
      bottom: testViewportHeight,
      right: testViewportWidth,
      x: 0,
      y: 0,
      toJSON: () => ({}),
    };
  };
}
