type Listener = () => void;

const listeners = new Set<Listener>();

/**
 * Says that the session is gone and cannot be recovered — a refresh was tried and refused.
 *
 * The API middleware discovers this, and the route guard is what has to act on it, but the
 * middleware runs inside `fetch` and holds no router. So it announces, and the guard listens.
 * Nothing else is allowed to subscribe: a second listener would mean two things deciding where
 * an expired session goes.
 */
export const sessionExpiry = {
  /** Registers a listener and returns the function that removes it. */
  subscribe(listener: Listener): () => void {
    listeners.add(listener);

    return () => {
      listeners.delete(listener);
    };
  },

  /** Tells every listener the session has ended. */
  announce(): void {
    for (const listener of [...listeners]) {
      listener();
    }
  },
};
