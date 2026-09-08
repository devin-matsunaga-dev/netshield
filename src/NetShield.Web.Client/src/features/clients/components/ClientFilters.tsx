import { Button } from '@/components/ui/Button';
import { Select } from '@/components/ui/Select';
import { TextField } from '@/components/ui/TextField';
import {
  hasActiveFilter,
  seenWindows,
  type ClientListFilters,
} from '@/features/clients/api/clientFilters';
import { seenWindowLabels } from '@/features/clients/components/clientLabels';

/**
 * One filter's new value. Every member is explicitly `| undefined` rather than merely optional,
 * because `exactOptionalPropertyTypes` tells "absent" and "present and undefined" apart — and
 * clearing a filter is the second of those: the key has to arrive so the router can remove the
 * search parameter rather than leave the previous value behind.
 */
export type ClientFilterChange = {
  [K in keyof ClientListFilters]?: ClientListFilters[K] | undefined;
};

interface ClientFiltersProps {
  readonly filters: ClientListFilters;
  /** Applies one change on top of what is already there, so filters compose. */
  readonly onChange: (change: ClientFilterChange) => void;
  readonly onClear: () => void;
}

/**
 * The client list's filters.
 *
 * Fewer than the device list's eight, and deliberately: a device carries attributes an operator
 * maintains and a client carries only what was observed, so there is nothing here to filter on
 * that somebody chose. What is here is the four questions the observations can answer — which
 * endpoint, which VLAN, how recently, and whether it holds an address at all.
 *
 * The search box takes a MAC in any spelling, an IP address, or the start of a hostname, and the
 * server decides which of the three it has. One box rather than three, because a person looking
 * for a client has exactly one of them and should not have to say which.
 */
export function ClientFilters({ filters, onChange, onClear }: ClientFiltersProps) {
  return (
    <div className="flex flex-wrap items-end gap-4">
      <div className="w-72">
        <TextField
          label="Search"
          type="search"
          placeholder="MAC address, IP address, hostname"
          value={filters.search ?? ''}
          onChange={(event) => {
            onChange({ search: event.target.value === '' ? undefined : event.target.value });
          }}
        />
      </div>

      <div className="w-32">
        <TextField
          label="VLAN"
          type="number"
          inputMode="numeric"
          min={1}
          max={4094}
          placeholder="Any"
          value={filters.vlanId?.toString() ?? ''}
          onChange={(event) => {
            const parsed = Number(event.target.value);

            onChange({
              vlanId: event.target.value === '' || !Number.isInteger(parsed) ? undefined : parsed,
            });
          }}
        />
      </div>

      <div className="w-48">
        <Select
          label="Last seen"
          anyLabel="Any time"
          value={filters.seen ?? ''}
          onChange={(event) => {
            onChange({
              seen: event.target.value === '' ? undefined : (event.target.value as never),
            });
          }}
          options={seenWindows.map((window) => ({
            value: window,
            label: seenWindowLabels[window],
          }))}
        />
      </div>

      <label className="flex h-control items-center gap-2 text-body text-secondary">
        <input
          type="checkbox"
          checked={filters.onlyActive === true}
          onChange={(event) => {
            onChange({ onlyActive: event.target.checked ? true : undefined });
          }}
          className="size-4 rounded-sm border-strong bg-raised accent-accent focus-visible:ring-2 focus-visible:ring-accent focus-visible:outline-none"
        />
        Holding an address
      </label>

      {hasActiveFilter(filters) && (
        <Button variant="ghost" onClick={onClear}>
          Clear filters
        </Button>
      )}
    </div>
  );
}
