import { describe, expect, it } from 'vitest';

import {
  hasActiveFilter,
  parseCredentialFilters,
} from '@/features/credentials/api/credentialFilters';
import {
  secretFieldsFor,
  usesSnmpAlgorithms,
  usesUsername,
} from '@/features/credentials/components/credentialLabels';

describe('parsing the credential filters', () => {
  it('reads the filters it knows', () => {
    expect(parseCredentialFilters({ kind: 'SnmpV3', search: '  core  ' })).toEqual({
      kind: 'SnmpV3',
      search: 'core',
    });
  });

  it('drops a kind it does not know rather than sending it', () => {
    // The fourth screen with URL state and the fourth time this matters: a value the route
    // rejected survives as the root parsed it, and without the second pass it reaches the API.
    expect(parseCredentialFilters({ kind: 'Telnet' })).toEqual({});
  });

  it('drops a search term that is only spaces', () => {
    expect(parseCredentialFilters({ search: '   ' })).toEqual({});
  });

  it('ignores anything it has never heard of', () => {
    expect(parseCredentialFilters({ colour: 'blue' })).toEqual({});
  });

  it('knows whether anything is narrowing the list', () => {
    expect(hasActiveFilter({})).toBe(false);
    expect(hasActiveFilter({ kind: 'SshKey' })).toBe(true);
  });
});

describe('which members a kind needs', () => {
  it('asks an SNMP v2c profile for a community string and nothing else', () => {
    expect(secretFieldsFor('SnmpV2c', 'None').map((field) => field.name)).toEqual(['community']);
    expect(usesUsername('SnmpV2c')).toBe(false);
    expect(usesSnmpAlgorithms('SnmpV2c')).toBe(false);
  });

  it('asks an SNMP v3 profile for a privacy password only when privacy is chosen', () => {
    expect(secretFieldsFor('SnmpV3', 'None').map((field) => field.name)).toEqual(['authPassword']);
    expect(secretFieldsFor('SnmpV3', 'Aes128').map((field) => field.name)).toEqual([
      'authPassword',
      'privacyPassword',
    ]);
  });

  it('asks an SSH key profile for a key, with an optional passphrase', () => {
    const fields = secretFieldsFor('SshKey', 'None');

    expect(fields.map((field) => field.name)).toEqual(['privateKey', 'privateKeyPassword']);
    expect(fields[0]?.multiline).toBe(true);
    expect(fields[1]?.optional).toBe(true);
  });

  it('asks an SSH password profile for a password', () => {
    expect(secretFieldsFor('SshPassword', 'None').map((field) => field.name)).toEqual(['password']);
    expect(usesUsername('SshPassword')).toBe(true);
  });
});
