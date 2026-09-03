import { useCallback, useEffect } from 'react';

import { usePersistentState } from '@/lib/usePersistentState';

/** Where the chosen theme is remembered between sessions. */
export const themeKey = 'netshield.theme';

/** Dark is the default and only complete theme; light is derived from it (DESIGN.md §2). */
export type Theme = 'dark' | 'light';

const parse = (raw: string): Theme | undefined =>
  raw === 'dark' || raw === 'light' ? raw : undefined;

const serialize = (value: Theme): string => value;

/**
 * The active theme, persisted and reflected onto the document as `data-theme`. Every colour in
 * the interface reads from a custom property that attribute selects, so nothing else has to
 * know which theme is on.
 */
export function useTheme(): readonly [Theme, () => void] {
  const [theme, setTheme] = usePersistentState<Theme>(themeKey, 'dark', parse, serialize);

  useEffect(() => {
    document.documentElement.dataset['theme'] = theme;
  }, [theme]);

  const toggle = useCallback(() => {
    setTheme(theme === 'dark' ? 'light' : 'dark');
  }, [theme, setTheme]);

  return [theme, toggle] as const;
}
