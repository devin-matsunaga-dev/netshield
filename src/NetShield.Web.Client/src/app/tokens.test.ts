import { describe, expect, it } from 'vitest';

import tailwindConfig from '../../tailwind.config';

const extend = tailwindConfig.theme?.extend ?? {};

/** A scale as written here is a plain record; Tailwind's type also admits a resolver function. */
function scaleOf(value: unknown): Record<string, unknown> {
  return typeof value === 'object' && value !== null ? (value as Record<string, unknown>) : {};
}

/**
 * Tailwind resolves `max-w-*`, `min-w-*` and `w-*` against the spacing scale before it reaches
 * the width scales, so a key present in both is silently answered by the spacing value. That is
 * not a theory: `maxWidth.content` was 1600px, `spacing.content` was 24px, and every content
 * column in the app rendered 24px wide — one word per line — because the spacing entry won.
 *
 * A name collision is the whole failure, so the name collision is what is asserted — for every
 * scale spacing shadows, not just the one that was caught.
 */
describe('the sizing scales', () => {
  const spacing = Object.keys(scaleOf(extend.spacing));

  for (const scale of ['maxWidth', 'minWidth', 'width'] as const) {
    it(`shares no key between spacing and ${scale}`, () => {
      const shadowed = Object.keys(scaleOf(extend[scale])).filter((key) => spacing.includes(key));

      expect(shadowed).toEqual([]);
    });
  }

  it('caps the content column at the DESIGN.md §5 width', () => {
    expect(scaleOf(extend.maxWidth)['page']).toBe('1600px');
  });
});
