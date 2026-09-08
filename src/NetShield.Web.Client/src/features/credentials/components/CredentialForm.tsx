import { useState, type SyntheticEvent } from 'react';

import type { Schemas } from '@/api/types';
import { Button } from '@/components/ui/Button';
import { FormMessage } from '@/components/ui/FormMessage';
import { Select } from '@/components/ui/Select';
import { TextField } from '@/components/ui/TextField';
import { credentialKinds } from '@/features/credentials/api/credentialFilters';
import type { CredentialRequestError } from '@/features/credentials/api/credentialMutations';
import {
  authAlgorithmLabels,
  authAlgorithms,
  type AuthAlgorithm,
  type PrivacyAlgorithm,
  kindLabels,
  privacyAlgorithmLabels,
  privacyAlgorithms,
  secretFieldsFor,
  usesSnmpAlgorithms,
  usesUsername,
} from '@/features/credentials/components/credentialLabels';
import { SecretFields } from '@/features/credentials/components/SecretFields';

type Material = Schemas['CredentialMaterial'];

/** Everything a person types about a profile. `material` is absent on an edit — see below. */
export interface CredentialFormValues {
  name: string;
  description: string;
  kind: Schemas['CredentialKind'];
  username: string;
  authAlgorithm: AuthAlgorithm;
  privacyAlgorithm: PrivacyAlgorithm;
  material: Material;
}

interface CredentialFormProps {
  readonly initial?: Partial<CredentialFormValues> | undefined;
  /**
   * Whether the kind may still be chosen. False on an edit: WP-1.2 fixed the kind at creation
   * because it decides what the sealed blob contains, so a profile whose kind changed would hold
   * material describing a protocol it no longer claims to be for.
   */
  readonly kindEditable: boolean;
  /**
   * Whether the secret fields are drawn. False on an edit, because the API returns no material
   * and a whole-resource PUT carries none — replacing a secret is its own action.
   */
  readonly withSecrets: boolean;
  readonly submitLabel: string;
  readonly pending: boolean;
  readonly error: CredentialRequestError | null;
  readonly onSubmit: (values: CredentialFormValues) => void;
  readonly onCancel: () => void;
}

const empty: CredentialFormValues = {
  name: '',
  description: '',
  kind: 'SnmpV2c',
  username: '',
  authAlgorithm: 'Sha256',
  privacyAlgorithm: 'Aes128',
  material: {},
};

/**
 * The create and edit form for a credential profile.
 *
 * One component for both, the way `DeviceForm` is, because the API's update is whole-resource
 * replacement — but with two differences the create/edit split does not have elsewhere. The kind
 * cannot be changed after creation, and the secret is not part of an edit at all: it is never
 * returned, so there is nothing to prefill and an absent value on a PUT would have to mean either
 * "unchanged" or "erased" with no way to tell which. WP-1.2 gave rotation a route of its own for
 * exactly that reason, and this form follows the shape rather than working around it.
 *
 * Which secret fields appear follows the chosen kind, and the SNMPv3 privacy password appears
 * only once a privacy algorithm other than `None` is chosen. That is presentation: the server
 * refuses a missing member, *and* one belonging to another kind, whatever this form drew.
 */
export function CredentialForm({
  initial,
  kindEditable,
  withSecrets,
  submitLabel,
  pending,
  error,
  onSubmit,
  onCancel,
}: CredentialFormProps) {
  const [values, setValues] = useState<CredentialFormValues>({ ...empty, ...initial });

  function set<K extends keyof CredentialFormValues>(key: K, value: CredentialFormValues[K]) {
    setValues((current) => ({ ...current, [key]: value }));
  }

  function setSecret(name: keyof Material, value: string) {
    setValues((current) => ({ ...current, material: { ...current.material, [name]: value } }));
  }

  function submit(event: SyntheticEvent) {
    event.preventDefault();
    onSubmit(values);
  }

  const fieldError = (name: string): string | undefined => error?.fieldErrors[name]?.[0];

  // A refusal already placed beside a field is not repeated at the foot of the form. A kind rule
  // is a 422 with a code and no field, so it lands here — which is right, because "an SNMPv3
  // profile needs an authentication password" is about the profile rather than one input.
  const unplacedError =
    error !== null && Object.keys(error.fieldErrors).length === 0 ? error.message : null;

  const secrets = secretFieldsFor(values.kind, values.privacyAlgorithm);

  return (
    <form onSubmit={submit} className="space-y-gutter" noValidate>
      <div className="grid grid-cols-1 gap-gutter md:grid-cols-2">
        <TextField
          label="Name"
          required
          hint="Unique among live profiles. What you will pick from on a device."
          value={values.name}
          error={fieldError('name')}
          onChange={(event) => {
            set('name', event.target.value);
          }}
        />

        {kindEditable ? (
          <Select
            label="Kind"
            value={values.kind}
            onChange={(event) => {
              // The secret members belong to the kind, so changing it clears what was typed
              // under the old one rather than sending a member the server would refuse.
              setValues((current) => ({
                ...current,
                kind: event.target.value as Schemas['CredentialKind'],
                material: {},
              }));
            }}
            options={credentialKinds.map((kind) => ({ value: kind, label: kindLabels[kind] }))}
          />
        ) : (
          <TextField
            label="Kind"
            readOnly
            disabled
            hint="Fixed when the profile was created — it decides what the stored secret contains."
            value={kindLabels[values.kind]}
          />
        )}

        <TextField
          label="Description"
          value={values.description}
          error={fieldError('description')}
          onChange={(event) => {
            set('description', event.target.value);
          }}
        />

        {usesUsername(values.kind) && (
          <TextField
            label="Username"
            required
            autoComplete="off"
            value={values.username}
            error={fieldError('username')}
            onChange={(event) => {
              set('username', event.target.value);
            }}
          />
        )}

        {usesSnmpAlgorithms(values.kind) && (
          <>
            <Select
              label="Authentication algorithm"
              value={values.authAlgorithm}
              onChange={(event) => {
                set('authAlgorithm', event.target.value as AuthAlgorithm);
              }}
              options={authAlgorithms.map((algorithm) => ({
                value: algorithm,
                label: authAlgorithmLabels[algorithm],
              }))}
            />
            <Select
              label="Privacy algorithm"
              value={values.privacyAlgorithm}
              onChange={(event) => {
                set('privacyAlgorithm', event.target.value as PrivacyAlgorithm);
              }}
              options={privacyAlgorithms.map((algorithm) => ({
                value: algorithm,
                label: privacyAlgorithmLabels[algorithm],
              }))}
            />
          </>
        )}

        {withSecrets && (
          <SecretFields
            fields={secrets}
            values={values.material}
            onChange={setSecret}
            fieldError={fieldError}
          />
        )}
      </div>

      {withSecrets && (
        <p className="text-metric-caption text-muted">
          The secret is sealed by the API as it arrives and is never returned by any response. You
          will not be able to read it back — replace it instead.
        </p>
      )}

      {unplacedError !== null && <FormMessage>{unplacedError}</FormMessage>}

      <div className="flex justify-end gap-2">
        <Button variant="ghost" type="button" onClick={onCancel}>
          Cancel
        </Button>
        <Button type="submit" disabled={pending}>
          {submitLabel}
        </Button>
      </div>
    </form>
  );
}
