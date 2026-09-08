import { createContext, useContext } from 'react';

/** A toast says what happened. Only two outcomes are worth one: it worked, or it did not. */
export type ToastTone = 'success' | 'danger';

export interface ToastApi {
  /** "Device added" — the past tense of the button that was pressed (DESIGN.md §8). */
  readonly announce: (message: string, tone?: ToastTone) => void;
}

/**
 * Where the toasts a screen has raised are held.
 *
 * Context, and deliberately so: ARCHITECTURE.md §9 keeps *server* state in TanStack Query, and
 * "the save you just made succeeded" is not server state — it is a sentence about something that
 * has already finished, which no query can be refetched to rediscover.
 *
 * Separate from the provider component so that the file holding the provider exports nothing but
 * a component, which is what fast refresh needs to keep working on it.
 */
export const ToastContext = createContext<ToastApi | null>(null);

/**
 * Raises a toast.
 *
 * Outside a provider it does nothing rather than throwing — a component rendered in isolation by
 * a test should not have to stand a provider up to be renderable, and a confirmation nobody sees
 * is a smaller failure than a screen that will not render.
 */
export function useToast(): ToastApi {
  return useContext(ToastContext) ?? noToasts;
}

const noToasts: ToastApi = { announce: () => undefined };
