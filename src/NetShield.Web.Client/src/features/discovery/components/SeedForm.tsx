import { useState, type SyntheticEvent } from 'react';

import { Button } from '@/components/ui/Button';
import { Modal } from '@/components/ui/Modal';
import { TextField } from '@/components/ui/TextField';
import type { DeviceRequestError } from '@/features/devices/api/deviceMutations';
import type { DiscoverySeedDetail } from '@/features/discovery/api/discoveryQueries';

export interface SeedFormValues {
  readonly name: string;
  readonly description: string | null;
  readonly enabled: boolean;
  readonly ranges: readonly string[];
  readonly exclusions: readonly string[];
  readonly intervalMinutes: number;
}

interface SeedFormProps {
  readonly seed?: DiscoverySeedDetail;
  readonly pending: boolean;
  readonly error: DeviceRequestError | null;
  readonly onSubmit: (values: SeedFormValues) => void;
  readonly onCancel: () => void;
}

/**
 * The ranges a discovery run sweeps.
 *
 * Ranges and exclusions are CIDR blocks, one per line — a list rather than a comma-separated
 * string, because an operator pasting a subnet plan out of a spreadsheet has one per line
 * already. The API is what validates them: it refuses ranges that overlap each other, and a seed
 * whose addresses exceed the configured ceiling, at the point of saving rather than at the point
 * of running. Restating those rules here would be a second copy to go stale.
 *
 * A /24 is 254 addresses: the network and broadcast addresses of a block of /30 or wider are not
 * probed, because pinging a broadcast address asks every host on the subnet to answer at once.
 */
export function SeedForm({ seed, pending, error, onSubmit, onCancel }: SeedFormProps) {
  const [name, setName] = useState(seed?.name ?? '');
  const [description, setDescription] = useState(seed?.description ?? '');
  const [enabled, setEnabled] = useState(seed?.enabled ?? true);
  const [ranges, setRanges] = useState((seed?.ranges ?? []).join('\n'));
  const [exclusions, setExclusions] = useState((seed?.exclusions ?? []).join('\n'));
  const [intervalMinutes, setIntervalMinutes] = useState(
    (seed?.intervalMinutes ?? 1440).toString(),
  );

  function submit(event: SyntheticEvent) {
    event.preventDefault();

    onSubmit({
      name: name.trim(),
      description: description.trim() === '' ? null : description.trim(),
      enabled,
      ranges: lines(ranges),
      exclusions: lines(exclusions),
      intervalMinutes: Number.parseInt(intervalMinutes, 10),
    });
  }

  const fieldError = (field: string): string | undefined => error?.fieldErrors[field]?.[0];

  return (
    <Modal title={seed === undefined ? 'Add seed' : `Edit ${seed.name}`} onClose={onCancel}>
      <form onSubmit={submit} className="space-y-gutter" noValidate>
        <TextField
          label="Name"
          required
          value={name}
          error={fieldError('name')}
          onChange={(event) => {
            setName(event.target.value);
          }}
        />

        <TextField
          label="Description"
          value={description}
          error={fieldError('description')}
          onChange={(event) => {
            setDescription(event.target.value);
          }}
        />

        <div className="space-y-1.5">
          <label htmlFor="seed-ranges" className="block text-metric-label text-secondary">
            Ranges
          </label>
          <textarea
            id="seed-ranges"
            rows={4}
            required
            value={ranges}
            placeholder="10.0.0.0/24"
            onChange={(event) => {
              setRanges(event.target.value);
            }}
            className="w-full rounded-control border border-strong bg-raised px-3 py-2 font-mono text-body text-primary placeholder:text-muted focus-visible:ring-2 focus-visible:ring-accent focus-visible:outline-none"
          />
          <p className="text-metric-caption text-muted">
            One CIDR block per line. They must not overlap each other.
          </p>
          {fieldError('ranges') !== undefined && (
            <p className="text-metric-caption text-danger">{fieldError('ranges')}</p>
          )}
        </div>

        <div className="space-y-1.5">
          <label htmlFor="seed-exclusions" className="block text-metric-label text-secondary">
            Exclusions
          </label>
          <textarea
            id="seed-exclusions"
            rows={3}
            value={exclusions}
            placeholder="10.0.0.128/25"
            onChange={(event) => {
              setExclusions(event.target.value);
            }}
            className="w-full rounded-control border border-strong bg-raised px-3 py-2 font-mono text-body text-primary placeholder:text-muted focus-visible:ring-2 focus-visible:ring-accent focus-visible:outline-none"
          />
          <p className="text-metric-caption text-muted">
            Addresses inside the ranges above that must never be probed.
          </p>
          {fieldError('exclusions') !== undefined && (
            <p className="text-metric-caption text-danger">{fieldError('exclusions')}</p>
          )}
        </div>

        <TextField
          label="Interval (minutes)"
          type="number"
          min={1}
          required
          hint="How often the schedule sweeps these ranges. 1440 is once a day."
          value={intervalMinutes}
          error={fieldError('intervalMinutes')}
          onChange={(event) => {
            setIntervalMinutes(event.target.value);
          }}
        />

        <label className="flex items-center gap-3">
          <input
            type="checkbox"
            checked={enabled}
            onChange={(event) => {
              setEnabled(event.target.checked);
            }}
            className="h-4 w-4 accent-accent focus-visible:ring-2 focus-visible:ring-accent focus-visible:outline-none"
          />
          {/* Disabling stops the schedule and not the seed: "Run now" still works on it. */}
          <span className="text-body text-primary">Sweep on a schedule</span>
        </label>

        {error !== null && Object.keys(error.fieldErrors).length === 0 && (
          <p role="alert" className="text-metric-caption text-danger">
            {error.message}
          </p>
        )}

        <div className="flex justify-end gap-2">
          <Button variant="ghost" onClick={onCancel}>
            Cancel
          </Button>
          <Button type="submit" disabled={pending}>
            {seed === undefined ? 'Add seed' : 'Save seed'}
          </Button>
        </div>
      </form>
    </Modal>
  );
}

/** One value per line, blanks and stray whitespace dropped. */
function lines(value: string): string[] {
  return value
    .split('\n')
    .map((line) => line.trim())
    .filter((line) => line.length > 0);
}
