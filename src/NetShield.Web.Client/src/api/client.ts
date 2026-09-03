import createClient from 'openapi-fetch';

import type { paths } from '@/api/schema';

/**
 * The only way the SPA talks to the API (ARCHITECTURE.md §9). Every path, body and response
 * shape below is checked against `src/api/schema.d.ts`, which is generated from the API's own
 * OpenAPI document — a hand-written `fetch` to an application endpoint would not be.
 */
export const api = createClient<paths>({
  // Same origin, resolved from the page rather than configured. The SPA is served by
  // NetShield.Web.Host in deployment and proxied to it by the Vite dev server in development, so
  // there is no address to carry and none to leak (SPEC.md §5). It is absolute rather than '/'
  // because `fetch` outside a browser will not parse a relative URL, and the test environment is
  // outside a browser.
  baseUrl: globalThis.location.origin,
  // The session and refresh cookies are HttpOnly; the browser attaches them and script cannot.
  credentials: 'same-origin',
  // Looked up per call rather than captured when this module loads. The client is created at
  // import time, and anything that replaces `fetch` afterwards — the request mock the tests run
  // against — would otherwise never be seen by it.
  fetch: (request) => globalThis.fetch(request),
});
