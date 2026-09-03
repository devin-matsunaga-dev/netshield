import { readFileSync } from 'node:fs';

import openapiTS, { astToString } from 'openapi-typescript';
import { describe, expect, it } from 'vitest';

/**
 * The second of the two gates CONVENTIONS.md §4 asks for. The first, in
 * `NetShield.UnitTests`, fails when the committed OpenAPI document stops describing the API;
 * this one fails when the checked-in client stops matching the committed document. Between them,
 * a drifted client is a failing build.
 */
describe('the generated API client', () => {
  it('matches the committed OpenAPI document', async () => {
    // Read and parsed here rather than handed over as a URL: the generator would fetch it, and
    // the request mock these tests run under refuses a request it has no handler for.
    const document: unknown = JSON.parse(
      readFileSync('../NetShield.Web.Host/openapi/v1.json', 'utf8'),
    );

    const regenerated = astToString(await openapiTS(document as Parameters<typeof openapiTS>[0]));

    expect(withoutBanner(readFileSync('src/api/schema.d.ts', 'utf8'))).toBe(
      withoutBanner(regenerated),
    );
  });
});

/** The generator's "do not edit" header, which only the command-line form writes. */
function withoutBanner(source: string): string {
  return source.replace(/^\/\*\*[\s\S]*?\*\/\s*/, '').trim();
}
