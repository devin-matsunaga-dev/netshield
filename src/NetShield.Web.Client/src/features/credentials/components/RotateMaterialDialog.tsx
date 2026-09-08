import { useState, type SyntheticEvent } from 'react';

import type { Schemas } from '@/api/types';
import { Button } from '@/components/ui/Button';
import { FormMessage } from '@/components/ui/FormMessage';
import { Modal } from '@/components/ui/Modal';
import { Select } from '@/components/ui/Select';
import type { CredentialRequestError } from '@/features/credentials/api/credentialMutations';
import {
  privacyAlgorithmLabels,
  privacyAlgorithms,
  secretFieldsFor,
  type PrivacyAlgorithm,
} from '@/features/credentials/components/credentialLabels';
import { SecretFields } from '@/features/credentials/components/SecretFields';

type Material = Schemas['CredentialMaterial'];

interface RotateMaterialDialogProps {
  readonly profile: Schemas['CredentialProfileSummary'];
  /**
   * The privacy algorithm the profile is stored with, which decides whether a privacy password is
   * one of its members. It is not editable here: changing it is an edit, and rotation replaces
   * the secret rather than the shape of the profile.
   */
  readonly privacyAlgorithm: PrivacyAlgorithm;
  readonly pending: boolean;
  readonly error: CredentialRequestError | null;
  readonly onConfirm: (material: Material) => void;
  readonly onCancel: () => void;
}

/**
 * Replacing a profile's secret.
 *
 * **It asks for the new value and never for the old one.** There is nothing to compare against —
 * the API returns no material, so this side could not check it — and an administrator replacing
 * a community string somebody else set should not have to know what it was. That is not a
 * weakening: reaching this dialog already requires `CredentialsManage`, which is the highest
 * privilege in the system, and the audit row records who did it.
 *
 * The form opens empty every time and is emptied again when it succeeds. Nothing typed here is
 * put in a query key, a URL or the cache.
 */
export function RotateMaterialDialog({
  profile,
  privacyAlgorithm,
  pending,
  error,
  onConfirm,
  onCancel,
}: RotateMaterialDialogProps) {
  const [material, setMaterial] = useState<Material>({});
  // Only meaningful for SNMP v3, where whether a privacy password is a member of the profile
  // depends on the algorithm it was stored with.
  const [privacy, setPrivacy] = useState(privacyAlgorithm);

  const fields = secretFieldsFor(profile.kind, privacy);
  const fieldError = (name: string): string | undefined => error?.fieldErrors[name]?.[0];
  const unplacedError =
    error !== null && Object.keys(error.fieldErrors).length === 0 ? error.message : null;

  function submit(event: SyntheticEvent) {
    event.preventDefault();
    onConfirm(material);
  }

  return (
    <Modal title={`Replace the credential for ${profile.name}`} onClose={onCancel}>
      <form onSubmit={submit} className="space-y-gutter" noValidate>
        <p className="text-body text-secondary">
          Enter the new value. NetShield cannot show you the current one — it is sealed and never
          returned — and does not need it to replace it.
        </p>

        {profile.kind === 'SnmpV3' && (
          <Select
            label="Privacy algorithm"
            value={privacy}
            onChange={(event) => {
              setPrivacy(event.target.value as PrivacyAlgorithm);
              setMaterial({});
            }}
            options={privacyAlgorithms.map((algorithm) => ({
              value: algorithm,
              label: privacyAlgorithmLabels[algorithm],
            }))}
          />
        )}

        <div className="grid grid-cols-1 gap-gutter">
          <SecretFields
            fields={fields}
            values={material}
            onChange={(name, value) => {
              setMaterial((current) => ({ ...current, [name]: value }));
            }}
            fieldError={fieldError}
          />
        </div>

        {profile.deviceCount !== 0 && (
          <p className="text-metric-caption text-muted">
            This profile reaches {String(profile.deviceCount)}{' '}
            {profile.deviceCount === 1 ? 'device' : 'devices'}. Every one of them will be reached
            with the new value from its next job onwards.
          </p>
        )}

        {unplacedError !== null && <FormMessage>{unplacedError}</FormMessage>}

        <div className="flex justify-end gap-2">
          <Button variant="ghost" type="button" onClick={onCancel}>
            Cancel
          </Button>
          <Button type="submit" disabled={pending}>
            Replace credential
          </Button>
        </div>
      </form>
    </Modal>
  );
}
