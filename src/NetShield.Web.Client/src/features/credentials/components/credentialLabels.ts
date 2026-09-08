import type { Schemas } from '@/api/types';

/**
 * The two SNMPv3 algorithm enums, with `null` stripped.
 *
 * The generated types are `… | null`, and that is a defect in the document rather than a fact
 * about the values: `CreateCredentialProfileRequest.authAlgorithm` is nullable, and ASP.NET Core
 * answers a nullable enum member by appending `null` to the *shared* enum schema — so every use
 * of the enum reads as nullable in the client although the API never sends one. WP-1.8 hit the
 * same thing on `ClientObservationSource` and avoided it by not adding a nullable member;
 * WP-1.2's request shapes already had one, so here it is stripped at the boundary instead. It is
 * recorded in STATUS.md, and the proper fix is a schema transformer where the document is made.
 */
export type AuthAlgorithm = NonNullable<Schemas['SnmpAuthAlgorithm']>;
export type PrivacyAlgorithm = NonNullable<Schemas['SnmpPrivacyAlgorithm']>;

/**
 * What each enum member is called on screen. The wire carries a name because WP-0.4 settled that
 * an enum travels as its name; a name is not a label, and sentence case is the rule
 * (DESIGN.md §4).
 */
export const kindLabels: Record<Schemas['CredentialKind'], string> = {
  SnmpV2c: 'SNMP v2c',
  SnmpV3: 'SNMP v3',
  SshPassword: 'SSH password',
  SshKey: 'SSH key',
};

export const authAlgorithmLabels: Record<AuthAlgorithm, string> = {
  Md5: 'MD5',
  Sha1: 'SHA-1',
  Sha224: 'SHA-224',
  Sha256: 'SHA-256',
  Sha384: 'SHA-384',
  Sha512: 'SHA-512',
};

export const privacyAlgorithmLabels: Record<PrivacyAlgorithm, string> = {
  None: 'None — authenticate only',
  Des: 'DES',
  Aes128: 'AES-128',
  Aes192: 'AES-192',
  Aes256: 'AES-256',
};

export const authAlgorithms: readonly AuthAlgorithm[] = [
  'Md5',
  'Sha1',
  'Sha224',
  'Sha256',
  'Sha384',
  'Sha512',
] as const;

export const privacyAlgorithms: readonly PrivacyAlgorithm[] = [
  'None',
  'Des',
  'Aes128',
  'Aes192',
  'Aes256',
] as const;

/**
 * Which secret members a kind needs, and what to call each one.
 *
 * The same rule `CredentialKindRules` enforces on the server, written out here so the form can
 * ask for the right fields — not so it can decide. The server answers a missing member, or one
 * belonging to another kind, with a 422 either way; this table only decides what is drawn, and
 * the refusal is still the server's to make.
 */
export interface SecretField {
  /** The member of `CredentialMaterial` this fills in. */
  readonly name:
    | 'community'
    | 'authPassword'
    | 'privacyPassword'
    | 'password'
    | 'privateKey'
    | 'privateKeyPassword';
  readonly label: string;
  readonly hint?: string;
  /** A key is many lines; everything else is one. */
  readonly multiline?: boolean;
  /** A passphrase on a key is genuinely optional; the rest are not. */
  readonly optional?: boolean;
}

export function secretFieldsFor(
  kind: Schemas['CredentialKind'],
  privacy: PrivacyAlgorithm,
): readonly SecretField[] {
  switch (kind) {
    case 'SnmpV2c':
      return [{ name: 'community', label: 'Community string' }];

    case 'SnmpV3':
      return [
        { name: 'authPassword', label: 'Authentication password' },
        // Only when privacy is asked for. WP-1.2 made `None` a member rather than an absent
        // algorithm, because "this profile authenticates and does not encrypt" is a decision
        // somebody made and a Phase 7 compliance rule will want to read it.
        ...(privacy === 'None'
          ? []
          : ([{ name: 'privacyPassword', label: 'Privacy password' }] as const)),
      ];

    case 'SshPassword':
      return [{ name: 'password', label: 'Password' }];

    case 'SshKey':
      return [
        { name: 'privateKey', label: 'Private key', multiline: true },
        {
          name: 'privateKeyPassword',
          label: 'Key passphrase',
          hint: 'Leave blank if the key has none.',
          optional: true,
        },
      ];
  }
}

/** Whether this kind authenticates as somebody, and so needs a username. */
export function usesUsername(kind: Schemas['CredentialKind']): boolean {
  return kind !== 'SnmpV2c';
}

/** Whether this kind carries SNMPv3's algorithm choices. */
export function usesSnmpAlgorithms(kind: Schemas['CredentialKind']): boolean {
  return kind === 'SnmpV3';
}
