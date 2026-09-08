import { describe, expect, it } from 'vitest';

import {
  hasActiveFilter,
  parseClientFilters,
  seenSince,
} from '@/features/clients/api/clientFilters';

/**
 * Reading the client list's filters out of a URL.
 *
 * This runs where the value is *used*, not only in the route's `validateSearch`, because a
 * TanStack Router route inherits its parent's search parameters and merges its own over them —
 * so a value the route rejected survives as the root parsed it. WP-0.7 found the trap on the
 * sign-in return path and WP-1.7 found it again on the device filters; these are the tests for
 * the third occurrence.
 */
describe('parsing the client filters', () => {
  it('reads every filter it knows', () => {
    expect(
      parseClientFilters({
        search: 'aa:bb:cc:00:00:21',
        deviceId: '019226b4-1000-7000-8000-000000000001',
        vlanId: '10',
        seen: '24h',
        onlyActive: 'true',
      }),
    ).toEqual({
      search: 'aa:bb:cc:00:00:21',
      deviceId: '019226b4-1000-7000-8000-000000000001',
      vlanId: 10,
      seen: '24h',
      onlyActive: true,
    });
  });

  it('drops a window it does not know', () => {
    expect(parseClientFilters({ seen: 'forever' })).toEqual({});
  });

  it('drops a device id that is not one', () => {
    // A value that is not a UUID cannot name a device, and sending it would earn a 400 from the
    // API rather than an unfiltered list.
    expect(parseClientFilters({ deviceId: 'core-sw-01' })).toEqual({});
  });

  it.each([
    ['melted', 'not a number'],
    ['0', 'below the first VLAN'],
    ['4095', 'above the last VLAN'],
    ['10.5', 'not a whole number'],
    ['-1', 'negative'],
    ['', 'empty'],
  ])('drops a VLAN of %s, which is %s', (value) => {
    expect(parseClientFilters({ vlanId: value })).toEqual({});
  });

  it.each([1, 4094])('keeps VLAN %i, which a tag can carry', (vlanId) => {
    expect(parseClientFilters({ vlanId: String(vlanId) })).toEqual({ vlanId });
  });

  it('trims a search term and drops one that is only spaces', () => {
    expect(parseClientFilters({ search: '  10.0.0.1  ' })).toEqual({ search: '10.0.0.1' });
    expect(parseClientFilters({ search: '   ' })).toEqual({});
  });

  it('reads onlyActive as a boolean or the string a URL carries', () => {
    expect(parseClientFilters({ onlyActive: true })).toEqual({ onlyActive: true });
    expect(parseClientFilters({ onlyActive: 'true' })).toEqual({ onlyActive: true });
    expect(parseClientFilters({ onlyActive: 'false' })).toEqual({});
  });

  it('ignores anything it has never heard of', () => {
    expect(parseClientFilters({ colour: 'blue', 'drop table': 'clients' })).toEqual({});
  });

  it('knows whether anything is narrowing the list', () => {
    expect(hasActiveFilter({})).toBe(false);
    expect(hasActiveFilter({ vlanId: 10 })).toBe(true);
    expect(hasActiveFilter({ onlyActive: true })).toBe(true);
  });
});

describe('the last-seen window', () => {
  const now = new Date('2026-09-08T12:00:00.000Z');

  it.each([
    ['1h', '2026-09-08T11:00:00.000Z'],
    ['24h', '2026-09-07T12:00:00.000Z'],
    ['7d', '2026-09-01T12:00:00.000Z'],
    ['30d', '2026-08-09T12:00:00.000Z'],
  ] as const)('resolves %s to %s', (window, expected) => {
    // Resolved where the query is built rather than held in the URL: "since 11:04" means
    // something different tomorrow, and a pasted link should keep meaning what it said.
    expect(seenSince(window, now)).toBe(expected);
  });
});
