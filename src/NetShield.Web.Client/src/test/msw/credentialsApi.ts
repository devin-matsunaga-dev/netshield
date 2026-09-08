import { http, HttpResponse, type RequestHandler } from 'msw';

import type { Schemas } from '@/api/types';

type CredentialProfileSummary = Schemas['CredentialProfileSummary'];
type CredentialProfileDetail = Schemas['CredentialProfileDetail'];

/**
 * The credential profile API a test sees.
 *
 * **It answers with no material, ever** — which is not a simplification but the contract: the API
 * never returns a secret in any response shape and `ApiSecretExposureTests` fails the build if
 * one appears. A fixture that returned one would let a screen render it and let a test pass while
 * doing so.
 *
 * `writes` records every request body the SPA sent, which is what lets a test assert both that a
 * secret reached the API when it should have and that nothing else did.
 */
export interface CredentialsApiState {
  profiles: CredentialProfileSummary[];
  detail: Map<string, CredentialProfileDetail>;
  /** Every write, in order, with the body as sent. */
  readonly writes: { method: string; path: string; body: unknown }[];
  /** A refusal the next write should answer with, to reach a form's error path. */
  nextRefusal: { status: number; code: string; detail: string } | null;
  failList: boolean;
}

const at = '2026-09-08T09:00:00.000Z';

export const snmpProfileId = '019226b4-5000-7000-8000-000000000001';
export const sshProfileId = '019226b4-5000-7000-8000-000000000002';

export function makeProfile(
  overrides: Partial<CredentialProfileSummary> = {},
): CredentialProfileSummary {
  return {
    id: snmpProfileId,
    name: 'Lab switches SNMP',
    kind: 'SnmpV2c',
    username: null,
    deviceCount: 3,
    materialUpdatedAt: at,
    updatedAt: at,
    ...overrides,
  };
}

export function createCredentialsApi(
  overrides: Partial<CredentialsApiState> = {},
): CredentialsApiState {
  return {
    profiles: [],
    detail: new Map(),
    writes: [],
    nextRefusal: null,
    failList: false,
    ...overrides,
  };
}

export function credentialHandlers(current: () => CredentialsApiState): RequestHandler[] {
  return [
    http.get('/api/v1/credential-profiles', ({ request }) => {
      const state = current();

      if (state.failList) {
        return HttpResponse.json({ status: 500, title: 'Server error' }, { status: 500 });
      }

      const query = new URL(request.url).searchParams;
      const kind = query.get('kind');
      const search = query.get('search');

      const matches = state.profiles.filter(
        (profile) =>
          (kind === null || profile.kind === kind) &&
          (search === null || profile.name.toLowerCase().includes(search.toLowerCase())),
      );

      return HttpResponse.json({ items: matches, nextCursor: null, totalCount: matches.length });
    }),

    http.post('/api/v1/credential-profiles', async ({ request }) => {
      const state = current();
      const body = await request.json();

      state.writes.push({ method: 'POST', path: '/credential-profiles', body });

      if (state.nextRefusal !== null) {
        return refuse(state);
      }

      const sent = body as {
        name: string;
        kind: CredentialProfileSummary['kind'];
        username?: string | null;
      };

      const created = makeProfile({
        id: `019226b4-5000-7000-8000-${String(state.profiles.length + 90).padStart(12, '0')}`,
        name: sent.name,
        kind: sent.kind,
        username: sent.username ?? null,
        deviceCount: 0,
      });

      state.profiles.push(created);

      // No material in the response — the API returns none and neither does this.
      return HttpResponse.json(created, { status: 201 });
    }),

    http.put('/api/v1/credential-profiles/:id/material', async ({ params, request }) => {
      const state = current();
      const body = await request.json();

      state.writes.push({
        method: 'PUT',
        path: `/credential-profiles/${String(params['id'])}/material`,
        body,
      });

      if (state.nextRefusal !== null) {
        return refuse(state);
      }

      const profile = state.profiles.find((candidate) => candidate.id === String(params['id']));

      return profile === undefined
        ? notFound('credential-profile.not-found')
        : HttpResponse.json({ ...profile, materialUpdatedAt: new Date().toISOString() });
    }),

    http.put('/api/v1/credential-profiles/:id', async ({ params, request }) => {
      const state = current();
      const body = await request.json();

      state.writes.push({
        method: 'PUT',
        path: `/credential-profiles/${String(params['id'])}`,
        body,
      });

      if (state.nextRefusal !== null) {
        return refuse(state);
      }

      const index = state.profiles.findIndex((candidate) => candidate.id === String(params['id']));
      const existing = state.profiles[index];

      if (existing === undefined) {
        return notFound('credential-profile.not-found');
      }

      const sent = body as { name: string; username?: string | null };
      const updated = { ...existing, name: sent.name, username: sent.username ?? null };

      state.profiles[index] = updated;

      return HttpResponse.json(updated);
    }),

    http.delete('/api/v1/credential-profiles/:id', ({ params }) => {
      const state = current();

      state.writes.push({
        method: 'DELETE',
        path: `/credential-profiles/${String(params['id'])}`,
        body: null,
      });
      state.profiles = state.profiles.filter((candidate) => candidate.id !== String(params['id']));

      return new HttpResponse(null, { status: 204 });
    }),
  ];
}

function refuse(state: CredentialsApiState) {
  const refusal = state.nextRefusal;

  state.nextRefusal = null;

  if (refusal === null) {
    return HttpResponse.json({ status: 500, title: 'Server error' }, { status: 500 });
  }

  return HttpResponse.json(
    {
      status: refusal.status,
      title: 'The request was refused.',
      detail: refusal.detail,
      code: refusal.code,
    },
    { status: refusal.status },
  );
}

function notFound(code: string) {
  return HttpResponse.json({ status: 404, title: 'Not found', code }, { status: 404 });
}
