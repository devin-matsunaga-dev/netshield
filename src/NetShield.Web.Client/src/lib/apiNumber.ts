/**
 * Reads a number the API sent.
 *
 * Every numeric member of the contract is generated as `number | string`, because the API's
 * OpenAPI document declares each one as `{"type": ["integer", "string"]}` — that is
 * `System.Text.Json` describing what it will *accept*, and it is written into the response
 * schemas as well as the request ones. NetShield always writes a number; nothing on the wire has
 * ever been a numeric string.
 *
 * So this coerces at the boundary rather than every call site guessing. It is a workaround for a
 * defect in how the document is produced, not a fact about the API, and it is recorded in
 * STATUS.md for the package that owns document generation to fix properly — at which point every
 * use of this can go.
 */
export function toNumber(value: number | string | null | undefined): number | null {
  if (value === null || value === undefined) {
    return null;
  }

  const parsed = typeof value === 'number' ? value : Number(value);

  return Number.isFinite(parsed) ? parsed : null;
}

/** The same, for a member the contract says is always present. */
export function requireNumber(value: number | string): number {
  return toNumber(value) ?? 0;
}

/** A round trip as "12.4 ms", or an em dash when the probe recorded none. */
export function formatRoundTrip(value: number | string | null | undefined): string {
  const milliseconds = toNumber(value);

  return milliseconds === null ? '—' : `${milliseconds.toFixed(1)} ms`;
}
