import { describe, expect, it } from 'vitest';

import {
  portOccupancySummary,
  portRoleExplanation,
} from '@/features/devices/components/portLabels';

const empty = {
  role: 'Empty' as const,
  roleReason: 'NoEvidence' as const,
  learnedAddressCount: null,
  clientCount: 0,
  clientsListed: true,
  neighbors: [],
};

describe('why a port was classified as it was', () => {
  it('names the evidence rather than the threshold', () => {
    // An operator who disagrees with the reading needs to know the port carries two hundred
    // addresses. That eight is the cut-off is a setting, and it is not what they act on first.
    expect(
      portRoleExplanation({
        ...empty,
        role: 'Uplink',
        roleReason: 'LearnedAddressCount',
        learnedAddressCount: 200,
      }),
    ).toBe('Carries 200 addresses for what is behind it.');
  });

  it('falls back to what NetShield holds when the device gave no count', () => {
    expect(
      portRoleExplanation({
        ...empty,
        role: 'Uplink',
        roleReason: 'LearnedAddressCount',
        clientCount: 12,
      }),
    ).toBe('Carries 12 addresses for what is behind it.');
  });

  it('writes one address in the singular', () => {
    expect(
      portRoleExplanation({
        ...empty,
        roleReason: 'LearnedAddressCount',
        learnedAddressCount: 1,
      }),
    ).toBe('Carries 1 address for what is behind it.');
  });

  it('says the topology decided when the far end is monitored', () => {
    expect(portRoleExplanation({ ...empty, roleReason: 'ManagedDevice' })).toBe(
      'Faces a monitored device.',
    );
  });

  it('says an empty port is empty rather than saying nothing', () => {
    expect(portRoleExplanation(empty)).toBe('Nothing has been observed on this port.');
  });
});

describe('what is on a port, for the column a reader scans', () => {
  it('counts an uplink rather than listing it', () => {
    expect(
      portOccupancySummary({
        ...empty,
        role: 'Uplink',
        clientsListed: false,
        clientCount: 187,
        learnedAddressCount: 200,
      }),
    ).toBe('Carrying 200 addresses');
  });

  it('names the far end of an uplink beside the count', () => {
    expect(
      portOccupancySummary({
        ...empty,
        role: 'Uplink',
        clientsListed: false,
        learnedAddressCount: 200,
        neighbors: [{ hostname: 'core-sw-1', systemName: 'core-sw-1' }],
      }),
    ).toBe('core-sw-1 — carrying 200 addresses');
  });

  it('names an unmonitored far end by what it called itself', () => {
    expect(
      portOccupancySummary({
        ...empty,
        role: 'Access',
        neighbors: [{ hostname: null, systemName: 'ap-floor-1' }],
      }),
    ).toBe('ap-floor-1');
  });

  it('counts the hosts on an access port', () => {
    expect(portOccupancySummary({ ...empty, role: 'Access', clientCount: 3 })).toBe('3 hosts');
  });

  it('writes one host in the singular', () => {
    expect(portOccupancySummary({ ...empty, role: 'Access', clientCount: 1 })).toBe('1 host');
  });

  it('lists a neighbour and the hosts together', () => {
    expect(
      portOccupancySummary({
        ...empty,
        role: 'Access',
        clientCount: 2,
        neighbors: [{ hostname: null, systemName: 'SEP001AA1112233' }],
      }),
    ).toBe('SEP001AA1112233, 2 hosts');
  });

  it('says an empty port is empty', () => {
    expect(portOccupancySummary(empty)).toBe('Nothing observed');
  });
});
