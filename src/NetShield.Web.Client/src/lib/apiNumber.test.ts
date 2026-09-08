import { describe, expect, it } from 'vitest';

import { formatRoundTrip, requireNumber, toNumber } from '@/lib/apiNumber';

/**
 * The contract types every numeric member as `number | string`, because the API's OpenAPI
 * document declares each one as `{"type": ["integer", "string"]}` — `System.Text.Json`
 * describing what it will accept, written into the response schemas as well as the request ones.
 * NetShield always writes a number; this is the boundary that says so once.
 */
describe('reading a number the API sent', () => {
  it('passes a number through', () => {
    expect(toNumber(42)).toBe(42);
    expect(toNumber(0)).toBe(0);
    expect(toNumber(-1.5)).toBe(-1.5);
  });

  it('reads the string form the contract also permits', () => {
    expect(toNumber('42')).toBe(42);
    expect(toNumber('1.25')).toBe(1.25);
  });

  it('is nothing for nothing, rather than zero', () => {
    // Zero round trips and no round trip at all are different facts, and only one is a warning.
    expect(toNumber(null)).toBeNull();
    expect(toNumber(undefined)).toBeNull();
  });

  it('is nothing for something that is not a number at all', () => {
    expect(toNumber('n/a')).toBeNull();
    expect(toNumber('')).toBe(0);
  });

  it('falls back to zero only where the contract promises a value', () => {
    expect(requireNumber(7)).toBe(7);
    expect(requireNumber('7')).toBe(7);
  });
});

describe('writing a round trip', () => {
  it('is one decimal place and a unit', () => {
    expect(formatRoundTrip(4.25)).toBe('4.3 ms');
    expect(formatRoundTrip('1')).toBe('1.0 ms');
  });

  it('is an em dash when nothing came back', () => {
    expect(formatRoundTrip(null)).toBe('—');
  });
});
