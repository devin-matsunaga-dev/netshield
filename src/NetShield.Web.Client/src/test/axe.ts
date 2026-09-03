import axe, { type AxeResults } from 'axe-core';
import { expect } from 'vitest';

/**
 * Fails with the rule and the element when the rendered tree has an accessibility violation.
 * CONVENTIONS.md §6 requires keyboard reach, a visible focus ring and a label on every icon-only
 * control; this catches the half of that a machine can see.
 */
export async function expectNoAccessibilityViolations(element: HTMLElement): Promise<void> {
  const results: AxeResults = await axe.run(element, {
    resultTypes: ['violations'],
    // jsdom loads no stylesheet, so every element is black on transparent and a contrast verdict
    // here would be about jsdom rather than about NetShield. Contrast is fixed by the DESIGN.md
    // §3 token pairs and checked against the reference screenshot instead.
    rules: { 'color-contrast': { enabled: false } },
  });

  const failures = results.violations.map(
    (violation) =>
      `${violation.id}: ${violation.help} (${violation.nodes.map((node) => node.target.join(' ')).join(', ')})`,
  );

  expect(failures).toEqual([]);
}
