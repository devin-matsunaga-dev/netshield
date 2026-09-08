import { useState, type SyntheticEvent } from 'react';

import type { Schemas } from '@/api/types';
import { Button } from '@/components/ui/Button';
import { Modal } from '@/components/ui/Modal';
import { Select } from '@/components/ui/Select';
import { TextField } from '@/components/ui/TextField';
import {
  criticalityTiers,
  deviceEnvironments,
  deviceRoles,
} from '@/features/devices/api/deviceFilters';
import type { DeviceRequestError } from '@/features/devices/api/deviceMutations';
import {
  criticalityLabels,
  environmentLabels,
  roleLabels,
} from '@/features/devices/components/deviceLabels';
import type { DiscoveryCandidateSummary } from '@/features/discovery/api/discoveryQueries';
import type { PromoteDiscoveryCandidateRequest } from '@/features/discovery/api/discoveryMutations';

interface PromoteCandidateDialogProps {
  readonly candidate: DiscoveryCandidateSummary;
  readonly pending: boolean;
  readonly error: DeviceRequestError | null;
  readonly onPromote: (request: PromoteDiscoveryCandidateRequest) => void;
  readonly onCancel: () => void;
}

/**
 * Turning a discovered address into a device.
 *
 * A candidate is a bare address and nothing else — the sweep asks one question, and identifying
 * a responder during it would mean carrying several credentials on one lease, which is a
 * collector-contract change. So this form asks for the hostname rather than offering one, and
 * every other field is the manual asset attributes SPEC.md §2 names.
 *
 * There is no vendor and no credential control. The device is created `Unknown` with nothing
 * assigned; a walk is what identifies it, and assigning a credential is behind a permission this
 * screen does not require.
 */
export function PromoteCandidateDialog({
  candidate,
  pending,
  error,
  onPromote,
  onCancel,
}: PromoteCandidateDialogProps) {
  const [hostname, setHostname] = useState('');
  const [site, setSite] = useState('');
  const [owner, setOwner] = useState('');
  const [role, setRole] = useState<Schemas['DeviceRole']>('Other');
  const [criticality, setCriticality] = useState<Schemas['CriticalityTier']>('Medium');
  const [environment, setEnvironment] = useState<Schemas['DeviceEnvironment']>('Production');

  function submit(event: SyntheticEvent) {
    event.preventDefault();

    onPromote({
      hostname: hostname.trim(),
      site: site.trim() === '' ? null : site.trim(),
      owner: owner.trim() === '' ? null : owner.trim(),
      role,
      criticality,
      environment,
      tags: [],
      notes: `Promoted from discovery candidate ${candidate.address}.`,
    });
  }

  return (
    <Modal title={`Promote ${candidate.address}`} onClose={onCancel}>
      <form onSubmit={submit} className="space-y-gutter" noValidate>
        <p className="text-body text-secondary">
          This address answered {candidate.timesSeen}{' '}
          {candidate.timesSeen === 1 ? 'sweep' : 'sweeps'}. Promoting it adds a device NetShield
          will start probing. Walk it afterwards to find out what it is — a sweep only established
          that something is there.
        </p>

        <TextField
          label="Hostname"
          required
          value={hostname}
          error={error?.fieldErrors['hostname']?.[0]}
          onChange={(event) => {
            setHostname(event.target.value);
          }}
        />

        <div className="grid grid-cols-1 gap-gutter sm:grid-cols-2">
          <Select
            label="Role"
            value={role}
            onChange={(event) => {
              setRole(event.target.value as Schemas['DeviceRole']);
            }}
            options={deviceRoles.map((value) => ({ value, label: roleLabels[value] }))}
          />
          <Select
            label="Criticality"
            value={criticality}
            onChange={(event) => {
              setCriticality(event.target.value as Schemas['CriticalityTier']);
            }}
            options={criticalityTiers.map((value) => ({
              value,
              label: criticalityLabels[value],
            }))}
          />
          <Select
            label="Environment"
            value={environment}
            onChange={(event) => {
              setEnvironment(event.target.value as Schemas['DeviceEnvironment']);
            }}
            options={deviceEnvironments.map((value) => ({
              value,
              label: environmentLabels[value],
            }))}
          />
          <TextField
            label="Site"
            value={site}
            onChange={(event) => {
              setSite(event.target.value);
            }}
          />
          <TextField
            label="Owner"
            value={owner}
            onChange={(event) => {
              setOwner(event.target.value);
            }}
          />
        </div>

        {error !== null && Object.keys(error.fieldErrors).length === 0 && (
          <p role="alert" className="text-metric-caption text-danger">
            {error.message}
          </p>
        )}

        <div className="flex justify-end gap-2">
          <Button variant="ghost" onClick={onCancel}>
            Cancel
          </Button>
          <Button type="submit" disabled={pending || hostname.trim() === ''}>
            Promote to device
          </Button>
        </div>
      </form>
    </Modal>
  );
}
