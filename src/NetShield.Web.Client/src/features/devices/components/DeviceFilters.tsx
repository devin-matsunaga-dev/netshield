import { Button } from '@/components/ui/Button';
import { Select } from '@/components/ui/Select';
import { TextField } from '@/components/ui/TextField';
import {
  criticalityTiers,
  deviceEnvironments,
  deviceRoles,
  deviceStates,
  deviceVendors,
  hasActiveFilter,
  type DeviceListFilters,
} from '@/features/devices/api/deviceFilters';
import {
  criticalityLabels,
  environmentLabels,
  roleLabels,
  vendorLabels,
} from '@/features/devices/components/deviceLabels';

/**
 * One filter's new value. Every member is explicitly `| undefined` rather than merely optional,
 * because `exactOptionalPropertyTypes` tells "absent" and "present and undefined" apart — and
 * clearing a filter is the second of those: the key has to arrive so the router can remove the
 * search parameter rather than leave the previous value behind.
 */
export type DeviceFilterChange = {
  [K in keyof DeviceListFilters]?: DeviceListFilters[K] | undefined;
};

interface DeviceFiltersProps {
  readonly filters: DeviceListFilters;
  /** Applies one change on top of what is already there, so filters compose. */
  readonly onChange: (change: DeviceFilterChange) => void;
  readonly onClear: () => void;
}

/**
 * The device list's filters (WP-1.7: state, vendor, site and criticality, plus the role,
 * environment, tag and free-text search the API already offers).
 *
 * Every one of them writes to the URL rather than to component state, which is what makes them
 * survive a refresh, a back button and a pasted link. `onChange` takes a partial so each control
 * knows only about its own field and the filters compose without any of them knowing the others
 * exist.
 *
 * Clearing a filter sends `undefined` rather than an empty string: the search parameter is then
 * removed from the address rather than left behind as `?state=`.
 */
export function DeviceFilters({ filters, onChange, onClear }: DeviceFiltersProps) {
  return (
    <div className="flex flex-wrap items-end gap-4">
      <div className="w-64">
        <TextField
          label="Search"
          type="search"
          placeholder="Hostname, address, serial"
          value={filters.search ?? ''}
          onChange={(event) => {
            onChange({ search: event.target.value === '' ? undefined : event.target.value });
          }}
        />
      </div>

      <div className="w-40">
        <Select
          label="State"
          anyLabel="Any state"
          value={filters.state ?? ''}
          onChange={(event) => {
            onChange({
              state: event.target.value === '' ? undefined : (event.target.value as never),
            });
          }}
          options={deviceStates.map((state) => ({ value: state, label: state }))}
        />
      </div>

      <div className="w-48">
        <Select
          label="Vendor"
          anyLabel="Any vendor"
          value={filters.vendor ?? ''}
          onChange={(event) => {
            onChange({
              vendor: event.target.value === '' ? undefined : (event.target.value as never),
            });
          }}
          options={deviceVendors.map((vendor) => ({
            value: vendor,
            label: vendorLabels[vendor],
          }))}
        />
      </div>

      <div className="w-40">
        <Select
          label="Role"
          anyLabel="Any role"
          value={filters.role ?? ''}
          onChange={(event) => {
            onChange({
              role: event.target.value === '' ? undefined : (event.target.value as never),
            });
          }}
          options={deviceRoles.map((role) => ({ value: role, label: roleLabels[role] }))}
        />
      </div>

      <div className="w-40">
        <Select
          label="Criticality"
          anyLabel="Any criticality"
          value={filters.criticality ?? ''}
          onChange={(event) => {
            onChange({
              criticality: event.target.value === '' ? undefined : (event.target.value as never),
            });
          }}
          options={criticalityTiers.map((tier) => ({
            value: tier,
            label: criticalityLabels[tier],
          }))}
        />
      </div>

      <div className="w-40">
        <Select
          label="Environment"
          anyLabel="Any environment"
          value={filters.environment ?? ''}
          onChange={(event) => {
            onChange({
              environment: event.target.value === '' ? undefined : (event.target.value as never),
            });
          }}
          options={deviceEnvironments.map((environment) => ({
            value: environment,
            label: environmentLabels[environment],
          }))}
        />
      </div>

      <div className="w-40">
        {/*
          Site is free text rather than a list: WP-1.1 settled that `site` is a string an
          operator types and not an entity, so there is nothing to enumerate. The API matches it
          exactly, ignoring case — two spellings are two sites until a site aggregate exists.
        */}
        <TextField
          label="Site"
          value={filters.site ?? ''}
          onChange={(event) => {
            onChange({ site: event.target.value === '' ? undefined : event.target.value });
          }}
        />
      </div>

      <div className="w-40">
        <TextField
          label="Tag"
          value={filters.tag ?? ''}
          onChange={(event) => {
            onChange({ tag: event.target.value === '' ? undefined : event.target.value });
          }}
        />
      </div>

      {hasActiveFilter(filters) && (
        <Button variant="ghost" onClick={onClear}>
          Clear filters
        </Button>
      )}
    </div>
  );
}
