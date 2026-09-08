import { describe, expect, it } from 'vitest';

import { formatTimestamp, formatTimestampTitle } from '@/lib/timestamp';

const now = new Date('2026-09-08T12:00:00.000Z');

/** DESIGN.md §8: relative under 24 hours, absolute beyond, and never "Invalid Date". */
describe('writing a time', () => {
  it('is relative in seconds, minutes and hours', () => {
    expect(formatTimestamp('2026-09-08T11:59:30.000Z', now)).toBe('30 sec ago');
    expect(formatTimestamp('2026-09-08T11:45:00.000Z', now)).toBe('15 min ago');
    expect(formatTimestamp('2026-09-08T09:00:00.000Z', now)).toBe('3 hours ago');
    expect(formatTimestamp('2026-09-08T11:00:00.000Z', now)).toBe('1 hour ago');
  });

  it('says "just now" rather than counting the first few seconds', () => {
    expect(formatTimestamp('2026-09-08T11:59:58.000Z', now)).toBe('just now');
  });

  /** A client clock a little ahead of the server's must not produce "in 3 seconds". */
  it('does not go negative when the clocks disagree', () => {
    expect(formatTimestamp('2026-09-08T12:00:05.000Z', now)).toBe('just now');
  });

  it('turns absolute past a day', () => {
    expect(formatTimestamp('2026-09-01T12:00:00.000Z', now)).not.toMatch(/ago/);
  });

  it('is an em dash for nothing, and for something that is not a time', () => {
    expect(formatTimestamp(null, now)).toBe('—');
    expect(formatTimestamp(undefined, now)).toBe('—');
    expect(formatTimestamp('not a date', now)).toBe('—');
  });
});

describe('the value shown on hover', () => {
  it('carries a timezone', () => {
    expect(formatTimestampTitle('2026-09-08T09:00:00.000Z')).toBeTruthy();
  });

  it('is absent when there is nothing to show', () => {
    expect(formatTimestampTitle(null)).toBeUndefined();
    expect(formatTimestampTitle('not a date')).toBeUndefined();
  });
});
