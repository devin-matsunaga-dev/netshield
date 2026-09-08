import type { Schemas } from '@/api/types';
import { TextField } from '@/components/ui/TextField';
import type { SecretField } from '@/features/credentials/components/credentialLabels';

type Material = Schemas['CredentialMaterial'];

interface SecretFieldsProps {
  readonly fields: readonly SecretField[];
  readonly values: Material;
  readonly onChange: (name: SecretField['name'], value: string) => void;
  readonly fieldError: (name: string) => string | undefined;
}

/**
 * The secret half of a credential form, and the one place in NetShield where a plaintext
 * credential is typed.
 *
 * **The rules this component exists to keep.** Every field is `type="password"` or a textarea
 * that is never prefilled; nothing here is ever given an initial value, because the API returns
 * no material and there would be nothing truthful to prefill it with. Nothing is written to a
 * query key, a URL, a `localStorage` entry or the query cache. Autofill is turned off — a browser
 * offering to remember a device's SNMP community is a copy of it in a password manager nobody
 * decided to make. The value lives in the parent's form state and in the request body, and the
 * parent clears the first as soon as the second succeeds.
 *
 * `SecretRedactor` covers the server's logs and `ApiSecretExposureTests` covers its responses
 * (WP-1.2). This is the half neither of those can reach, so it is kept by hand and asserted by
 * `Credentials.test.tsx`.
 */
export function SecretFields({ fields, values, onChange, fieldError }: SecretFieldsProps) {
  return (
    <>
      {fields.map((field) =>
        field.multiline === true ? (
          <MultilineSecret
            key={field.name}
            field={field}
            value={values[field.name] ?? ''}
            onChange={onChange}
            error={fieldError(field.name)}
          />
        ) : (
          <TextField
            key={field.name}
            label={field.label}
            type="password"
            required={field.optional !== true}
            autoComplete="off"
            spellCheck={false}
            hint={field.hint}
            error={fieldError(field.name)}
            // Never a `defaultValue`: the API returns no material, so there is nothing to
            // prefill and an empty field is the honest one.
            value={values[field.name] ?? ''}
            onChange={(event) => {
              onChange(field.name, event.target.value);
            }}
          />
        ),
      )}
    </>
  );
}

/**
 * A private key, which is many lines and cannot be a password input.
 *
 * It is still never prefilled and still not autofilled. `spellCheck` is off because a browser
 * spell-checking a private key sends it to a spell-check service on some platforms.
 */
function MultilineSecret({
  field,
  value,
  onChange,
  error,
}: {
  readonly field: SecretField;
  readonly value: string;
  readonly onChange: (name: SecretField['name'], value: string) => void;
  readonly error: string | undefined;
}) {
  const id = `secret-${field.name}`;
  const errorId = `${id}-error`;

  return (
    <div className="space-y-1.5 md:col-span-2">
      <label htmlFor={id} className="block text-metric-label text-secondary">
        {field.label}
      </label>
      <textarea
        id={id}
        rows={6}
        required={field.optional !== true}
        autoComplete="off"
        spellCheck={false}
        aria-invalid={error !== undefined}
        aria-describedby={error === undefined ? undefined : errorId}
        value={value}
        onChange={(event) => {
          onChange(field.name, event.target.value);
        }}
        className="w-full rounded-control border border-strong bg-raised px-3 py-2 font-mono text-table-cell text-primary placeholder:text-muted focus-visible:ring-2 focus-visible:ring-accent focus-visible:outline-none"
      />
      {error !== undefined && (
        <p id={errorId} className="text-metric-caption text-danger">
          {error}
        </p>
      )}
    </div>
  );
}
