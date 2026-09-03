import { useCallback, useState } from 'react';

/**
 * State that survives a reload, kept in `localStorage`.
 *
 * Reads and writes are guarded: a browser with storage disabled, or a private window, throws on
 * access rather than returning null, and a preference is never worth failing a render over.
 */
export function usePersistentState<T>(
  key: string,
  fallback: T,
  parse: (raw: string) => T | undefined,
  serialize: (value: T) => string,
): readonly [T, (value: T) => void] {
  const [value, setValue] = useState<T>(() => {
    try {
      const raw = window.localStorage.getItem(key);

      return raw === null ? fallback : (parse(raw) ?? fallback);
    } catch {
      return fallback;
    }
  });

  const store = useCallback(
    (next: T) => {
      setValue(next);

      try {
        window.localStorage.setItem(key, serialize(next));
      } catch {
        // A preference that could not be remembered is not a failure the user needs told about.
      }
    },
    [key, serialize],
  );

  return [value, store] as const;
}
