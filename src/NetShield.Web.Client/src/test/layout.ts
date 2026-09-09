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
  /*
    The viewport's scale, which React Flow divides a measured node by so that a tile measured on
    a zoomed canvas still reports its unzoomed size. jsdom implements no `DOMMatrixReadOnly` at
    all, and the exception is thrown inside a ResizeObserver callback where nothing can catch it
    — so the whole measurement pass dies and no edge is ever positioned.

    Identity is the honest answer here: jsdom computes no transform, so there is no zoom to
    undo. Only `m22` is read.
  */
  vi.stubGlobal(
    'DOMMatrixReadOnly',
    class {
      public readonly m11 = 1;

      public readonly m22 = 1;
    },
  );

  vi.stubGlobal(
    'ResizeObserver',
    class {
      private readonly targets = new Set<Element>();

      public constructor(private readonly callback: ResizeObserverCallback) {}

      /**
       * Announces the element's size once, on the next microtask.
       *
       * jsdom never resizes anything, so a stub that only recorded the element would hand React
       * Flow a node it had never measured — and React Flow positions an edge from the *handle*
       * bounds it takes during that measurement, so every tile would draw and not one link
       * between them would. Reporting once is the whole of what a browser does here: one initial
       * observation, and then nothing, because nothing moves.
       */
      public observe(target: Element): void {
        this.targets.add(target);

        queueMicrotask(() => {
          if (!this.targets.has(target)) {
            return;
          }

          const box = { inlineSize: testViewportWidth, blockSize: testViewportHeight };

          this.callback(
            [
              {
                target,
                contentRect: target.getBoundingClientRect(),
                borderBoxSize: [box],
                contentBoxSize: [box],
                devicePixelContentBoxSize: [box],
              },
            ],
            this,
          );
        });
      }

      public unobserve(target: Element): void {
        this.targets.delete(target);
      }

      public disconnect(): void {
        this.targets.clear();
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
