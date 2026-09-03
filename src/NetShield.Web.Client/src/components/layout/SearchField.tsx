import { Search } from 'lucide-react';
import { useEffect, useRef } from 'react';

/**
 * The header's search field (DESIGN.md §5): raised surface, 8px radius, magnifier on the left and
 * the ⌘K chip right-aligned inside, capped at 400px.
 *
 * The shortcut focuses the field. What a search actually queries arrives with the features it
 * searches; nothing is submitted here.
 */
export function SearchField() {
  const field = useRef<HTMLInputElement>(null);

  useEffect(() => {
    function onKeyDown(event: KeyboardEvent) {
      if (event.key.toLowerCase() !== 'k' || !(event.metaKey || event.ctrlKey)) {
        return;
      }

      event.preventDefault();
      field.current?.focus();
    }

    window.addEventListener('keydown', onKeyDown);

    return () => {
      window.removeEventListener('keydown', onKeyDown);
    };
  }, []);

  return (
    <div className="relative w-full max-w-search">
      <Search aria-hidden className="absolute top-1/2 left-3 size-4 -translate-y-1/2 text-muted" />
      <input
        ref={field}
        type="search"
        aria-label="Search for devices, clients, alerts"
        placeholder="Search for devices, clients, alerts..."
        className="h-control w-full rounded-control border border-subtle bg-raised pr-16 pl-9 text-body text-primary placeholder:text-muted"
      />
      <kbd
        aria-hidden
        className="absolute top-1/2 right-2 -translate-y-1/2 rounded border border-subtle bg-surface px-1.5 py-0.5 text-badge text-muted"
      >
        ⌘ K
      </kbd>
    </div>
  );
}
