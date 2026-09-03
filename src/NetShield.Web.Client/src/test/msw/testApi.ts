import { http, HttpResponse, type RequestHandler } from 'msw';

import type { AuthenticatedUser, Permission } from '@/api/types';

/**
 * Enough of the authentication API to drive the SPA through a real session (WP-0.7).
 *
 * It models the two things the client actually reacts to: whether the session cookie is still
 * accepted, and whether the refresh cookie can mint a new one. That is what makes "refreshes
 * once and then redirects" testable through the DOM rather than by reaching into the client.
 *
 * The permission lists here are fixture data — what the server said this session holds — and not
 * a copy of `RolePermissions`. That table has one home, on the server, and
 * `SessionPermissionTests` is what holds a role against it.
 */
export interface TestApiState {
  /** Who is signed in, or nobody. */
  user: AuthenticatedUser | null;
  /** The password `POST /auth/login` and `POST /auth/password` accept. */
  password: string;
  /** Whether `GET /auth/me` still accepts the session cookie. */
  sessionValid: boolean;
  /** Whether `POST /auth/refresh` will mint a new session. */
  refreshAllowed: boolean;
  /** Every call the SPA made, in order, for asserting that a refresh happened exactly once. */
  readonly calls: string[];
}

/** Every permission, which is what the Administrator holds. */
export const allPermissions: readonly Permission[] = [
  'InventoryRead',
  'InventoryWrite',
  'CredentialsManage',
  'DiscoveryRun',
  'TopologyRead',
  'TelemetryRead',
  'FlowsRead',
  'LogsRead',
  'AlertsRead',
  'AlertsManage',
  'AlertRulesWrite',
  'ConfigsRead',
  'ConfigsManage',
  'ComplianceRead',
  'ComplianceManage',
  'VulnerabilitiesRead',
  'VulnerabilitiesManage',
  'ReportsRead',
  'ReportsManage',
  'PoliciesWrite',
  'AuditRead',
  'SystemAdminister',
];

/** The reads a Read-only session holds, and nothing that changes anything. */
export const readOnlyPermissions: readonly Permission[] = [
  'InventoryRead',
  'TopologyRead',
  'TelemetryRead',
  'FlowsRead',
  'LogsRead',
  'AlertsRead',
  'ConfigsRead',
  'ComplianceRead',
  'VulnerabilitiesRead',
  'ReportsRead',
];

/** The session the SPA sees unless a test says otherwise. */
export const testUser: AuthenticatedUser = {
  id: '019226b4-0000-7000-8000-000000000001',
  username: 'admin',
  displayName: 'Ada Lovelace',
  role: 'Administrator',
  mustChangePassword: false,
  permissions: [...allPermissions],
};

/** A session that may read everything and change nothing. */
export const readOnlyUser: AuthenticatedUser = {
  id: '019226b4-0000-7000-8000-000000000002',
  username: 'viewer',
  displayName: 'Ben Okri',
  role: 'ReadOnly',
  mustChangePassword: false,
  permissions: [...readOnlyPermissions],
};

const password = 'Correct-Horse-42';

/** A fresh API, signed in as `testUser`, which is what most tests want. */
export function createTestApi(overrides: Partial<TestApiState> = {}): TestApiState {
  return {
    user: { ...testUser },
    password,
    sessionValid: true,
    refreshAllowed: true,
    calls: [],
    ...overrides,
  };
}

/** The single 401 body the API answers every refused sign-in with. */
function unauthorized(code: string) {
  return HttpResponse.json(
    {
      status: 401,
      title: 'Unauthenticated',
      detail: 'The username or password is incorrect.',
      code,
    },
    { status: 401 },
  );
}

/**
 * MSW handlers reading whatever `current()` returns, so a test can replace the whole state
 * between runs — or mutate the one it has — without re-registering a handler.
 */
export function authHandlers(current: () => TestApiState): RequestHandler[] {
  return [
    http.get('/api/v1/auth/me', () => {
      const state = current();

      state.calls.push('GET /me');

      return state.sessionValid && state.user !== null
        ? HttpResponse.json(state.user)
        : unauthorized('identity.no-session');
    }),

    http.post('/api/v1/auth/login', async ({ request }) => {
      const state = current();

      state.calls.push('POST /login');

      const body = (await request.json()) as { username: string; password: string };

      if (state.user === null || body.password !== state.password) {
        return unauthorized('identity.invalid-credentials');
      }

      state.sessionValid = true;

      return HttpResponse.json(state.user);
    }),

    http.post('/api/v1/auth/refresh', () => {
      const state = current();

      state.calls.push('POST /refresh');

      if (!state.refreshAllowed || state.user === null) {
        state.sessionValid = false;

        return unauthorized('identity.invalid-credentials');
      }

      state.sessionValid = true;

      return HttpResponse.json(state.user);
    }),

    http.post('/api/v1/auth/logout', () => {
      const state = current();

      state.calls.push('POST /logout');
      state.sessionValid = false;
      state.user = null;

      return new HttpResponse(null, { status: 204 });
    }),

    http.post('/api/v1/auth/password', async ({ request }) => {
      const state = current();

      state.calls.push('POST /password');

      const body = (await request.json()) as { currentPassword: string; newPassword: string };

      if (state.user === null) {
        return unauthorized('identity.no-session');
      }

      if (body.currentPassword !== state.password) {
        return problem(
          422,
          'identity.current-password-invalid',
          'The current password is not correct.',
        );
      }

      if (body.newPassword === state.password) {
        return problem(
          422,
          'identity.password-unchanged',
          'The new password must be different from the current one.',
        );
      }

      if (body.newPassword.length < 12) {
        return HttpResponse.json(
          {
            status: 422,
            title: 'Unprocessable entity',
            detail: 'That password does not meet the password policy.',
            code: 'identity.password-policy',
            errors: { newPassword: ['It must be at least 12 characters long.'] },
          },
          { status: 422 },
        );
      }

      state.password = body.newPassword;
      state.user = { ...state.user, mustChangePassword: false };
      state.sessionValid = true;

      return HttpResponse.json(state.user);
    }),
  ];
}

function problem(status: number, code: string, detail: string) {
  return HttpResponse.json({ status, title: 'Unprocessable entity', detail, code }, { status });
}
