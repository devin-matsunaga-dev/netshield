import type { Schemas } from '@/api/types';

/**
 * The query-key factory for the discovery feature (CONVENTIONS.md §6).
 *
 * Candidates, runs, seeds and the ignore list each hang off `all`, so promoting a candidate —
 * which creates a device and settles a candidate at once — can invalidate everything discovery
 * knows with one key, and the device keys separately.
 */
export const discoveryKeys = {
  all: ['discovery'] as const,
  candidates: (status: Schemas['DiscoveryCandidateStatus'] | undefined) =>
    [...discoveryKeys.all, 'candidates', status ?? 'any'] as const,
  runs: (seedId: string | undefined) => [...discoveryKeys.all, 'runs', seedId ?? 'any'] as const,
  run: (id: string) => [...discoveryKeys.all, 'run', id] as const,
  runHosts: (id: string, outcome: Schemas['DiscoveryHostOutcome'] | undefined) =>
    [...discoveryKeys.run(id), 'hosts', outcome ?? 'any'] as const,
  seeds: () => [...discoveryKeys.all, 'seeds'] as const,
  ignores: () => [...discoveryKeys.all, 'ignores'] as const,
};
