import { http, HttpResponse } from 'msw';

import type { AuthenticatedUser } from '@/api/types';

/** The session the SPA sees unless a test says otherwise. */
export const testUser: AuthenticatedUser = {
  id: '019226b4-0000-7000-8000-000000000001',
  username: 'admin',
  displayName: 'Ada Lovelace',
  role: 'Administrator',
  mustChangePassword: false,
};

/**
 * The default API. Every handler answers the shape `src/api/schema.d.ts` describes, so a fixture
 * that drifts from the contract fails to type-check rather than passing a test that lies.
 */
export const handlers = [http.get('/api/v1/auth/me', () => HttpResponse.json(testUser))];
