import { describe, expect, it } from 'vitest';

import {
  describePort,
  formatDuration,
  formatOui,
  observationSourceLabels,
} from '@/features/clients/components/clientLabels';

describe('naming an observation source', () => {
  it('writes every member in sentence case', () => {
    // The wire carries a name because WP-0.4 settled that an enum travels as its name. A name is
    // not a label, and DESIGN.md §4 admits no tracked-out or camel-cased ones.
    expect(observationSourceLabels.ArpTable).toBe('ARP table');
    expect(observationSourceLabels.MacAddressTable).toBe('MAC address table');
    expect(observationSourceLabels.DhcpLease).toBe('DHCP lease');
    expect(observationSourceLabels.WirelessAssociation).toBe('Wireless association');
  });
});

describe('showing an OUI', () => {
  it('shows the prefix as itself, because the registry is not in this repository', () => {
    expect(formatOui('AA:BB:CC', false)).toBe('AA:BB:CC');
  });

  it('names a locally administered address rather than a prefix that stands for nothing', () => {
    // Nobody registered it, so there is no vendor a registry could name. That is what address
    // randomisation on a phone looks like, and it is a different fact from "not found".
    expect(formatOui('AA:BB:CC', true)).toBe('Locally administered');
  });
});

describe('how long a binding lasted', () => {
  const from = '2026-09-08T12:00:00.000Z';

  it.each([
    ['2026-09-08T12:00:30.000Z', '30 s'],
    ['2026-09-08T12:09:00.000Z', '9 min'],
    ['2026-09-08T15:00:00.000Z', '3 h'],
    ['2026-09-12T12:00:00.000Z', '4 d'],
  ])('writes an interval ending %s as %s', (to, expected) => {
    // The largest unit that still says something: four days does not need its minutes.
    expect(formatDuration(from, to)).toBe(expected);
  });

  it('measures an open interval against now', () => {
    expect(formatDuration(new Date(Date.now() - 5_000).toISOString(), null)).toBe('5 s');
  });

  it('answers with an em dash rather than a negative for an interval that runs backwards', () => {
    expect(formatDuration(from, '2026-09-08T11:00:00.000Z')).toBe('—');
    expect(formatDuration('not-a-time', null)).toBe('—');
  });
});

describe('describing a port', () => {
  it('names an access port when one address was learned on it', () => {
    expect(describePort(1)).toBe('Access port — 1 address learned');
  });

  it('reads the count out for a port carrying many', () => {
    // Evidence rather than a guess at the topology: which port is the edge needs the graph
    // Phase 2 builds, and until then the count is what a reader can judge from.
    expect(describePort(214)).toBe('214 addresses learned on this port');
  });

  it('says nothing when the device did not say', () => {
    expect(describePort(null)).toBeNull();
    expect(describePort(undefined)).toBeNull();
  });
});
