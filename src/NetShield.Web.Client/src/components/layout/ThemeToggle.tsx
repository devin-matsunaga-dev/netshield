import { Moon, Sun } from 'lucide-react';

import { useTheme } from '@/lib/useTheme';

/**
 * Switches between the dark console and its derived light palette (DESIGN.md §2). The choice is
 * remembered; nothing else in the interface knows which theme is on.
 */
export function ThemeToggle() {
  const [theme, toggle] = useTheme();
  const Icon = theme === 'dark' ? Moon : Sun;

  return (
    <button
      type="button"
      onClick={toggle}
      aria-label={theme === 'dark' ? 'Switch to light theme' : 'Switch to dark theme'}
      className="flex size-9 items-center justify-center rounded-control text-secondary transition-colors duration-hover hover:bg-raised hover:text-primary"
    >
      <Icon aria-hidden className="size-5" />
    </button>
  );
}
