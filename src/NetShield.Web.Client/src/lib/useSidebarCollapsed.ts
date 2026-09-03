import { useCallback } from 'react';

import { usePersistentState } from '@/lib/usePersistentState';

/** Where the sidebar's collapsed state is remembered between sessions. */
export const sidebarCollapsedKey = 'netshield.sidebar.collapsed';

const parse = (raw: string): boolean | undefined =>
  raw === 'true' ? true : raw === 'false' ? false : undefined;

const serialize = (value: boolean): string => String(value);

/** The sidebar's collapsed state, persisted (DESIGN.md §5). */
export function useSidebarCollapsed(): readonly [boolean, () => void] {
  const [collapsed, setCollapsed] = usePersistentState(
    sidebarCollapsedKey,
    false,
    parse,
    serialize,
  );

  const toggle = useCallback(() => {
    setCollapsed(!collapsed);
  }, [collapsed, setCollapsed]);

  return [collapsed, toggle] as const;
}
