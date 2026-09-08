import { useState, type SyntheticEvent } from 'react';

import type { Schemas } from '@/api/types';
import { Button } from '@/components/ui/Button';
import { Select } from '@/components/ui/Select';
import { TextField } from '@/components/ui/TextField';
import {
  criticalityTiers,
  deviceEnvironments,
  deviceRoles,
  deviceVendors,
} from '@/features/devices/api/deviceFilters';
import type { DeviceRequestError } from '@/features/devices/api/deviceMutations';
import {
  criticalityLabels,
  environmentLabels,
  roleLabels,
  vendorLabels,
} from '@/features/devices/components/deviceLabels';

/** Everything a person types about a device. `state` is absent, deliberately — see below. */
export interface DeviceFormValues {
  hostname: string;
  primaryIpAddress: string;
  vendor: Schemas['DeviceVendor'];
  model: string;
  osVersion: string;
  serialNumber: string;
  site: string;
  role: Schemas['DeviceRole'];
  criticality: Schemas['CriticalityTier'];
  environment: Schemas['DeviceEnvironment'];
  owner: string;
  tags: string;
  notes: string;
}

interface DeviceFormProps {
  readonly initial?: Partial<DeviceFormValues>;
  readonly submitLabel: string;
  readonly pending: boolean;
  readonly error: DeviceRequestError | null;
  readonly onSubmit: (values: DeviceFormValues) => void;
  readonly onCancel: () => void;
}

const empty: DeviceFormValues = {
  hostname: '',
  primaryIpAddress: '',
  vendor: 'Unknown',
  model: '',
  osVersion: '',
  serialNumber: '',
  site: '',
  role: 'Other',
  criticality: 'Medium',
  environment: 'Production',
  owner: '',
  tags: '',
  notes: '',
};

/**
 * The add and edit form. One component for both, because the API's update is whole-resource
 * replacement (WP-1.1): an edit sends every field, exactly as a create does, so the two differ
 * only in where the values start and which verb is sent.
 *
 * There is no state control. Reachability is something NetShield observes and WP-1.4 owns every
 * transition — the request shape has no member for it, which is a rule that cannot be forgotten
 * at a call site.
 *
 * The button keeps its word through the flow (DESIGN.md §8): "Add device" → "Device added".
 */
export function DeviceForm({
  initial,
  submitLabel,
  pending,
  error,
  onSubmit,
  onCancel,
}: DeviceFormProps) {
  const [values, setValues] = useState<DeviceFormValues>({ ...empty, ...initial });

  function set<K extends keyof DeviceFormValues>(key: K, value: DeviceFormValues[K]) {
    setValues((current) => ({ ...current, [key]: value }));
  }

  function submit(event: SyntheticEvent) {
    event.preventDefault();
    onSubmit(values);
  }

  // The server's own words, beside the field they are about. A duplicate address is a `409` with
  // a code rather than a field error, so it is matched by code and placed by hand.
  const placedByCode = error !== null && error.code === 'device.duplicate-primary-ip';

  const fieldError = (name: string): string | undefined =>
    error?.fieldErrors[name]?.[0] ??
    (name === 'primaryIpAddress' && placedByCode ? error.message : undefined);

  // A refusal that has already been put beside a field is not repeated at the foot of the form.
  const unplacedError =
    error !== null && !placedByCode && Object.keys(error.fieldErrors).length === 0
      ? error.message
      : null;

  return (
    <form onSubmit={submit} className="space-y-gutter" noValidate>
      <div className="grid grid-cols-1 gap-gutter md:grid-cols-2">
        <TextField
          label="Hostname"
          required
          value={values.hostname}
          error={fieldError('hostname')}
          onChange={(event) => {
            set('hostname', event.target.value);
          }}
        />
        <TextField
          label="Primary IP address"
          required
          hint="The address NetShield reaches this device on. Unique among live devices."
          value={values.primaryIpAddress}
          error={fieldError('primaryIpAddress')}
          onChange={(event) => {
            set('primaryIpAddress', event.target.value);
          }}
        />
        <Select
          label="Vendor"
          value={values.vendor}
          onChange={(event) => {
            set('vendor', event.target.value as Schemas['DeviceVendor']);
          }}
          options={deviceVendors.map((vendor) => ({ value: vendor, label: vendorLabels[vendor] }))}
        />
        <Select
          label="Role"
          value={values.role}
          onChange={(event) => {
            set('role', event.target.value as Schemas['DeviceRole']);
          }}
          options={deviceRoles.map((role) => ({ value: role, label: roleLabels[role] }))}
        />
        <TextField
          label="Model"
          value={values.model}
          error={fieldError('model')}
          onChange={(event) => {
            set('model', event.target.value);
          }}
        />
        <TextField
          label="OS version"
          value={values.osVersion}
          error={fieldError('osVersion')}
          onChange={(event) => {
            set('osVersion', event.target.value);
          }}
        />
        <TextField
          label="Serial number"
          value={values.serialNumber}
          error={fieldError('serialNumber')}
          onChange={(event) => {
            set('serialNumber', event.target.value);
          }}
        />
        <TextField
          label="Site"
          value={values.site}
          error={fieldError('site')}
          onChange={(event) => {
            set('site', event.target.value);
          }}
        />
        <Select
          label="Criticality"
          value={values.criticality}
          onChange={(event) => {
            set('criticality', event.target.value as Schemas['CriticalityTier']);
          }}
          options={criticalityTiers.map((tier) => ({
            value: tier,
            label: criticalityLabels[tier],
          }))}
        />
        <Select
          label="Environment"
          value={values.environment}
          onChange={(event) => {
            set('environment', event.target.value as Schemas['DeviceEnvironment']);
          }}
          options={deviceEnvironments.map((environment) => ({
            value: environment,
            label: environmentLabels[environment],
          }))}
        />
        <TextField
          label="Owner"
          value={values.owner}
          error={fieldError('owner')}
          onChange={(event) => {
            set('owner', event.target.value);
          }}
        />
        <TextField
          label="Tags"
          hint="Separated by commas. Lower-cased and sorted when saved."
          value={values.tags}
          error={fieldError('tags')}
          onChange={(event) => {
            set('tags', event.target.value);
          }}
        />
      </div>

      <div className="space-y-1.5">
        <label htmlFor="device-notes" className="block text-metric-label text-secondary">
          Notes
        </label>
        <textarea
          id="device-notes"
          rows={4}
          value={values.notes}
          onChange={(event) => {
            set('notes', event.target.value);
          }}
          className="w-full rounded-control border border-strong bg-raised px-3 py-2 text-body text-primary placeholder:text-muted focus-visible:ring-2 focus-visible:ring-accent focus-visible:outline-none"
        />
      </div>

      {/*
        A refusal with no field to sit beside — a 409, or something the server said about the
        request as a whole. It says what failed; the server's wording is preferred to any
        written here, because whoever knows why owns the sentence.
      */}
      {unplacedError !== null && (
        <p role="alert" className="text-metric-caption text-danger">
          {unplacedError}
        </p>
      )}

      <div className="flex justify-end gap-2">
        <Button variant="ghost" onClick={onCancel}>
          Cancel
        </Button>
        <Button type="submit" disabled={pending}>
          {submitLabel}
        </Button>
      </div>
    </form>
  );
}
