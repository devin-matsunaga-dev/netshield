import { useCallback, useMemo, useState, type ReactNode } from 'react';

import { ToastContext, type ToastApi, type ToastTone } from '@/lib/toast';

import { cn } from '@/lib/cn';

interface Toast {
  readonly id: number;
  readonly message: string;
  readonly tone: ToastTone;
}

/**
 * Holds the toasts a screen has raised and draws the region they appear in.
 *
 * The context and the hook live in `src/lib/toast.ts`; this file is the component alone.
 */
export function ToastProvider({ children }: { readonly children: ReactNode }) {
  const [toasts, setToasts] = useState<readonly Toast[]>([]);

  const announce = useCallback((message: string, tone: ToastTone = 'success') => {
    // A counter rather than a timestamp: two toasts raised in one tick would share a key.
    setToasts((current) => [...current, { id: (current.at(-1)?.id ?? 0) + 1, message, tone }]);
  }, []);

  const dismiss = useCallback((id: number) => {
    setToasts((current) => current.filter((toast) => toast.id !== id));
  }, []);

  const api = useMemo<ToastApi>(() => ({ announce }), [announce]);

  return (
    <ToastContext value={api}>
      {children}
      {/*
        Polite rather than assertive: a confirmation should not interrupt whatever a screen
        reader is in the middle of saying. It is dismissed by the reader rather than on a timer —
        a message that removes itself is one somebody was always going to miss.
      */}
      <div
        aria-live="polite"
        className="pointer-events-none fixed right-content bottom-content z-50 flex flex-col gap-2"
      >
        {toasts.map((toast) => (
          <div
            key={toast.id}
            className={cn(
              'pointer-events-auto flex items-center gap-3 rounded-card border bg-surface py-2 pr-2 pl-4',
              toast.tone === 'success' ? 'border-success' : 'border-danger',
            )}
          >
            <span
              className={cn('text-body', toast.tone === 'success' ? 'text-success' : 'text-danger')}
            >
              {toast.message}
            </span>
            <button
              type="button"
              aria-label={`Dismiss: ${toast.message}`}
              onClick={() => {
                dismiss(toast.id);
              }}
              className="rounded-control px-2 py-1 text-metric-caption text-muted transition-colors duration-hover hover:bg-raised hover:text-primary focus-visible:ring-2 focus-visible:ring-accent focus-visible:outline-none"
            >
              Dismiss
            </button>
          </div>
        ))}
      </div>
    </ToastContext>
  );
}
