import { describe, expect, it } from 'vitest';

import { defaultReturnTo, safeReturnPath } from '@/features/auth/returnPath';

describe('the return path after signing in', () => {
  it.each(['/devices', '/reports/bandwidth', '/administration/audit-log?filter=login'])(
    'keeps %s, which is where the guard turned the reader away from',
    (path) => {
      expect(safeReturnPath(path)).toBe(path);
    },
  );

  it.each([
    ['https://example.invalid/steal', 'an absolute URL'],
    ['//example.invalid/steal', 'a protocol-relative URL'],
    ['/\\example.invalid/steal', 'a backslash form several browsers read as protocol-relative'],
    ['devices', 'a relative path, which would resolve against whatever page it lands on'],
    ['', 'an empty value'],
  ])('discards %s — %s', (path) => {
    expect(safeReturnPath(path)).toBe(defaultReturnTo);
  });

  it.each([undefined, null, 42, {}, ['/devices']])('discards %s, which is not a path', (value) => {
    expect(safeReturnPath(value)).toBe(defaultReturnTo);
  });
});
