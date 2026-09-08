import { useMutation, useQueryClient } from '@tanstack/react-query';

import { api } from '@/api/client';
import type { Schemas } from '@/api/types';
import { deviceKeys } from '@/features/devices/api/deviceKeys';
import { DeviceRequestError } from '@/features/devices/api/deviceMutations';
import { discoveryKeys } from '@/features/discovery/api/discoveryKeys';

export type PromoteDiscoveryCandidateRequest = Schemas['PromoteDiscoveryCandidateRequest'];
export type CreateDiscoverySeedRequest = Schemas['CreateDiscoverySeedRequest'];
export type UpdateDiscoverySeedRequest = Schemas['UpdateDiscoverySeedRequest'];

/**
 * Turns a candidate into a device.
 *
 * The device is created with vendor and state `Unknown` and no credential profile, whatever this
 * form says about anything else — a sweep established only that something answered, and a
 * promotion that assigned a credential would be a way around the `CredentialsManage` boundary
 * WP-1.2 drew. Fingerprinting is what fills the rest in.
 */
export function usePromoteCandidate() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async ({
      id,
      request,
    }: {
      id: string;
      request: PromoteDiscoveryCandidateRequest;
    }) => {
      const { data, error, response } = await api.POST(
        '/api/v1/discovery/candidates/{id}/promote',
        { params: { path: { id } }, body: request },
      );

      if (!response.ok || data === undefined) {
        throw toRequestError(error, 'The candidate could not be promoted.');
      }

      return data;
    },
    onSuccess: async () => {
      // Both sides move: a candidate is settled and a device now exists.
      await queryClient.invalidateQueries({ queryKey: discoveryKeys.all });
      await queryClient.invalidateQueries({ queryKey: deviceKeys.all });
    },
  });
}

/**
 * Dismisses a candidate permanently, by adding its address to the ignore list.
 *
 * Deleting the ignore entry later does not revive the candidate — the next sweep that finds the
 * address answering is what puts it back, so one rule decides what is reviewable rather than two.
 */
export function useIgnoreCandidate() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (id: string) => {
      const { error, response } = await api.POST('/api/v1/discovery/candidates/{id}/ignore', {
        params: { path: { id } },
      });

      if (!response.ok) {
        throw toRequestError(error, 'The candidate could not be ignored.');
      }
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: discoveryKeys.all }),
  });
}

/** Starts a run of one seed now, outside its schedule. Behind `DiscoveryRun`. */
export function useStartDiscoveryRun() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (seedId: string) => {
      const { data, error, response } = await api.POST('/api/v1/discovery/runs', {
        params: { query: { seedId } },
      });

      if (!response.ok || data === undefined) {
        throw toRequestError(error, 'The discovery run could not be started.');
      }

      return data;
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: discoveryKeys.all }),
  });
}

export function useCreateSeed() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (request: CreateDiscoverySeedRequest) => {
      const { data, error, response } = await api.POST('/api/v1/discovery/seeds', {
        body: request,
      });

      if (!response.ok || data === undefined) {
        throw toRequestError(error, 'The seed could not be saved.');
      }

      return data;
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: discoveryKeys.seeds() }),
  });
}

export function useUpdateSeed() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async ({ id, request }: { id: string; request: UpdateDiscoverySeedRequest }) => {
      const { data, error, response } = await api.PUT('/api/v1/discovery/seeds/{id}', {
        params: { path: { id } },
        body: request,
      });

      if (!response.ok || data === undefined) {
        throw toRequestError(error, 'The seed could not be saved.');
      }

      return data;
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: discoveryKeys.seeds() }),
  });
}

export function useDeleteSeed() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (id: string) => {
      const { error, response } = await api.DELETE('/api/v1/discovery/seeds/{id}', {
        params: { path: { id } },
      });

      if (!response.ok) {
        throw toRequestError(error, 'The seed could not be removed.');
      }
    },
    // Runs keep their own copy of the seed's name and ranges, so history survives the delete —
    // but the run list's join to a live seed does not, so both are refreshed.
    onSuccess: () => queryClient.invalidateQueries({ queryKey: discoveryKeys.all }),
  });
}

export function useCreateIgnore() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (request: Schemas['CreateDiscoveryIgnoreRequest']) => {
      const { data, error, response } = await api.POST('/api/v1/discovery/ignores', {
        body: request,
      });

      if (!response.ok || data === undefined) {
        throw toRequestError(error, 'The address could not be added to the ignore list.');
      }

      return data;
    },
    // An ignore entry also settles the candidates it covers, so the review list moves too.
    onSuccess: () => queryClient.invalidateQueries({ queryKey: discoveryKeys.all }),
  });
}

export function useDeleteIgnore() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (id: string) => {
      const { error, response } = await api.DELETE('/api/v1/discovery/ignores/{id}', {
        params: { path: { id } },
      });

      if (!response.ok) {
        throw toRequestError(error, 'The ignore entry could not be removed.');
      }
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: discoveryKeys.ignores() }),
  });
}

function toRequestError(error: unknown, fallback: string): DeviceRequestError {
  // Structural, for the reason `deviceMutations` reads a problem the same way: the document
  // declares no `code`, and `code` is what every refusal is told apart by.
  const problem = error as
    { detail?: string; code?: string; errors?: Record<string, string[]> } | undefined;

  return new DeviceRequestError(problem?.detail ?? fallback, problem?.code, problem?.errors ?? {});
}
