import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';

import { server } from '@/test/msw/server';
import { renderApp } from '@/test/renderApp';

/** The shape a dev server with no `/api` proxy answers with: the SPA shell, and a 200. */
function servesTheShellForApiPaths() {
  server.use(
    http.get('/api/v1/auth/me', () =>
      HttpResponse.html('<!doctype html><html><body><div id="root"></div></body></html>'),
    ),
  );
}

describe('an API that cannot be read', () => {
  it('says what failed rather than showing a parser message', async () => {
    // This is what a dev server with no /api proxy actually returns, and what the reader met
    // before this screen existed: TanStack Router's default error page, showing
    // "Unexpected token '<'".
    servesTheShellForApiPaths();

    renderApp('/overview');

    expect(
      await screen.findByRole('heading', { level: 1, name: 'NetShield is not responding' }),
    ).toBeVisible();
    expect(screen.queryByText(/Unexpected token/)).not.toBeInTheDocument();
  });

  it('offers a retry, and the retry works once the API does', async () => {
    const user = userEvent.setup();
    servesTheShellForApiPaths();

    renderApp('/overview');
    await screen.findByRole('heading', { level: 1, name: 'NetShield is not responding' });

    // The API comes back — the proxy starts, or the service finishes starting.
    server.resetHandlers();

    await user.click(screen.getByRole('button', { name: 'Try again' }));

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Network overview' }),
    ).toBeVisible();
  });

  it('is not shown for a session that is merely signed out', async () => {
    server.use(
      http.get('/api/v1/auth/me', () =>
        HttpResponse.json({ status: 401, title: 'Unauthenticated' }, { status: 401 }),
      ),
      http.post('/api/v1/auth/refresh', () =>
        HttpResponse.json({ status: 401, title: 'Unauthenticated' }, { status: 401 }),
      ),
    );

    renderApp('/overview');

    expect(await screen.findByRole('heading', { level: 1, name: 'Sign in' })).toBeVisible();
  });
});
