import { useEffect, useRef, type ReactNode } from 'react';

interface ModalProps {
  readonly title: string;
  readonly onClose: () => void;
  readonly children: ReactNode;
}

/**
 * A modal panel (DESIGN.md §6): the panel surface, a hairline border, 12px radius, no shadow —
 * separation comes from the border and the scrim, never from a drop shadow (DESIGN.md §9.4).
 *
 * Built on the native `<dialog>` element, so the browser owns the focus trap, the inert
 * background and Escape. Every hand-written trap is a chance to get one of the three wrong, and
 * this one is not written here.
 */
export function Modal({ title, onClose, children }: ModalProps) {
  const dialog = useRef<HTMLDialogElement>(null);

  useEffect(() => {
    const element = dialog.current;

    if (element === null || element.open) {
      return;
    }

    if (typeof element.showModal === 'function') {
      element.showModal();

      return;
    }

    // jsdom implements `<dialog>` but not `showModal`. Opening it by hand keeps the element in
    // the accessibility tree — a closed `<dialog>` exposes no `dialog` role at all — so a test
    // reads the same panel a browser draws, without the focus trap that only a browser has.
    element.open = true;
  }, []);

  return (
    <dialog
      ref={dialog}
      aria-label={title}
      onCancel={(event) => {
        event.preventDefault();
        onClose();
      }}
      className="m-auto w-full max-w-lg rounded-card border border-subtle bg-surface p-0 text-primary backdrop:bg-black/60 open:block"
    >
      <div className="flex h-card-header items-center border-b border-subtle px-gutter">
        <h2 className="text-card-title text-primary">{title}</h2>
      </div>
      <div className="p-gutter">{children}</div>
    </dialog>
  );
}
