import { useId, useState } from 'react';

import { Button } from '@/components/ui/Button';
import { Modal } from '@/components/ui/Modal';

interface ConfirmDeleteProps {
  readonly title: string;
  /** What is about to happen, in a sentence. */
  readonly description: string;
  /** The word the reader has to type. The name of the thing, so it cannot be typed by habit. */
  readonly confirmWord: string;
  readonly actionLabel: string;
  readonly pending?: boolean;
  readonly onConfirm: () => void;
  readonly onCancel: () => void;
}

/**
 * The typed confirmation DESIGN.md §6 requires before anything irreversible.
 *
 * The reader types the device's own hostname rather than the word "delete": a fixed word is one
 * a pair of hands learns to type without reading, and the whole point is that they read which
 * device this is.
 */
export function ConfirmDelete({
  title,
  description,
  confirmWord,
  actionLabel,
  pending = false,
  onConfirm,
  onCancel,
}: ConfirmDeleteProps) {
  const [typed, setTyped] = useState('');
  const id = useId();
  const matches = typed === confirmWord;

  return (
    <Modal title={title} onClose={onCancel}>
      <div className="space-y-4">
        <p className="text-body text-secondary">{description}</p>
        <div className="space-y-1.5">
          <label htmlFor={id} className="block text-metric-label text-secondary">
            Type <span className="font-mono text-primary">{confirmWord}</span> to confirm
          </label>
          <input
            id={id}
            value={typed}
            autoComplete="off"
            onChange={(event) => {
              setTyped(event.target.value);
            }}
            className="h-control w-full rounded-control border border-strong bg-raised px-3 font-mono text-body text-primary focus-visible:ring-2 focus-visible:ring-accent focus-visible:outline-none"
          />
        </div>
        <div className="flex justify-end gap-2">
          <Button variant="ghost" onClick={onCancel}>
            Cancel
          </Button>
          <button
            type="button"
            disabled={!matches || pending}
            onClick={onConfirm}
            className="inline-flex h-control items-center justify-center rounded-control bg-danger px-4 text-nav-item text-white transition-colors duration-hover hover:opacity-90 focus-visible:ring-2 focus-visible:ring-accent focus-visible:outline-none disabled:cursor-not-allowed disabled:opacity-50"
          >
            {actionLabel}
          </button>
        </div>
      </div>
    </Modal>
  );
}
