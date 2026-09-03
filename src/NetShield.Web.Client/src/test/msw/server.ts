import { setupServer } from 'msw/node';

import { handlers } from '@/test/msw/handlers';

/** The API, mocked at the network boundary (CONVENTIONS.md §7). */
export const server = setupServer(...handlers);
