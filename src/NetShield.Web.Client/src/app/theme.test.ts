import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join } from 'node:path';

import { describe, expect, it } from 'vitest';

import tailwindConfig from '../../tailwind.config';

const designDocument = readFileSync('../../docs/DESIGN.md', 'utf8');
const themeSheet = readFileSync('src/styles/theme.css', 'utf8');

/** The `:root` block, which is the dark palette and the only one DESIGN.md §3 specifies. */
const darkPalette = themeSheet.slice(0, themeSheet.indexOf("[data-theme='light']"));

/** Every colour DESIGN.md §3 names, lower-cased. */
const designTokens = section(designDocument, '## 3. Color tokens', '## 4. Typography')
  .match(/#[0-9a-fA-F]{6,8}\b/g)
  ?.map((hex) => hex.toLowerCase());

describe('the colour tokens', () => {
  it('carries every value DESIGN.md §3 names', () => {
    expect(designTokens).toBeDefined();
    expect(designTokens?.length).toBeGreaterThan(20);

    const missing = [...new Set(designTokens)].filter((hex) => !darkPalette.includes(hex));

    expect(missing).toEqual([]);
  });

  it('introduces no colour DESIGN.md §3 does not name', () => {
    // The light palette is exempt: DESIGN.md §2 derives it from these tokens rather than
    // specifying it, so its values are in theme.css and in no table.
    const undeclared = [...new Set(darkPalette.match(/#[0-9a-fA-F]{3,8}\b/g) ?? [])].filter(
      (hex) => !designTokens?.includes(hex.toLowerCase()),
    );

    expect(undeclared).toEqual([]);
  });

  it('exposes every surface, border and text role as a Tailwind token', () => {
    const theme = tailwindConfig.theme?.extend ?? {};

    expect(Object.keys(theme.backgroundColor ?? {})).toEqual(
      expect.arrayContaining(['base', 'sidebar', 'surface', 'raised']),
    );
    expect(Object.keys(theme.borderColor ?? {})).toEqual(
      expect.arrayContaining(['subtle', 'strong']),
    );
    expect(Object.keys(theme.textColor ?? {})).toEqual(
      expect.arrayContaining(['primary', 'secondary', 'muted']),
    );
  });

  it('exposes every semantic colour and its tint', () => {
    const colors = Object.keys(tailwindConfig.theme?.extend?.colors ?? {});

    for (const semantic of ['accent', 'success', 'warning', 'danger', 'info', 'violet', 'orange']) {
      expect(colors).toContain(semantic);
      expect(colors).toContain(`${semantic}-tint`);
    }

    expect(colors).toEqual(
      expect.arrayContaining(['chart-1', 'chart-2', 'chart-3', 'chart-4', 'chart-5']),
    );
  });

  it('exposes every type role DESIGN.md §4 names', () => {
    const sizes = Object.keys(tailwindConfig.theme?.extend?.fontSize ?? {});

    expect(sizes).toEqual(
      expect.arrayContaining([
        'page-title',
        'page-subtitle',
        'card-title',
        'metric-value',
        'metric-label',
        'metric-caption',
        'body',
        'table-header',
        'table-cell',
        'badge',
        'nav-item',
      ]),
    );
  });
});

describe('the components', () => {
  it('never place a raw hex value in one (DESIGN.md §9.3)', () => {
    const offenders = sourceFiles('src')
      .filter((file) => /#[0-9a-fA-F]{3,8}\b/.test(readFileSync(file, 'utf8')))
      .map((file) => file.replace(/\\/g, '/'));

    expect(offenders).toEqual([]);
  });
});

/** The text of a Markdown section, from one heading up to the next. */
function section(document: string, from: string, to: string): string {
  const start = document.indexOf(from);
  const end = document.indexOf(to, start);

  return document.slice(start, end === -1 ? undefined : end);
}

/** Every hand-written TypeScript file under a directory. Generated files are not ours. */
function sourceFiles(directory: string): string[] {
  const generated = new Set(['routeTree.gen.ts', 'schema.d.ts']);

  return readdirSync(directory).flatMap((entry) => {
    const path = join(directory, entry);

    if (statSync(path).isDirectory()) {
      return sourceFiles(path);
    }

    return /\.tsx?$/.test(entry) && !generated.has(entry) ? [path] : [];
  });
}
