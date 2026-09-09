import { useMutation, useQueryClient } from '@tanstack/react-query';

import { api } from '@/api/client';
import type { Schemas } from '@/api/types';
import { deviceKeys } from '@/features/devices/api/deviceKeys';

export type CreateDeviceRequest = Schemas['CreateDeviceRequest'];
export type UpdateDeviceRequest = Schemas['UpdateDeviceRequest'];

/**
 * An API refusal a form can act on: the problem's `code` says which rule was broken and
 * `errors` says which field it was about.
 *
 * Carried rather than flattened to a string, because a duplicate address belongs beside the
 * address field and a policy failure belongs beside the field it names — the same reason
 * WP-0.7's change-password screen branches on `code` rather than on the status.
 */
export class DeviceRequestError extends Error {
  public constructor(
    message: string,
    public readonly code: string | undefined,
    public readonly fieldErrors: Record<string, string[]>,
  ) {
    super(message);
    this.name = 'DeviceRequestError';
  }
}

export function useCreateDevice() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (request: CreateDeviceRequest) => {
      const { data, error, response } = await api.POST('/api/v1/devices', { body: request });

      if (!response.ok || data === undefined) {
        throw toRequestError(error, 'The device could not be added.');
      }

      return data;
    },
    // Every list, whatever it is filtered by: a new device may or may not match the one that is
    // mounted, and only the server can say.
    onSuccess: () => queryClient.invalidateQueries({ queryKey: deviceKeys.lists() }),
  });
}

export function useUpdateDevice(id: string) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (request: UpdateDeviceRequest) => {
      const { data, error, response } = await api.PUT('/api/v1/devices/{id}', {
        params: { path: { id } },
        body: request,
      });

      if (!response.ok || data === undefined) {
        throw toRequestError(error, 'The device could not be saved.');
      }

      return data;
    },
    onSuccess: async (device) => {
      queryClient.setQueryData(deviceKeys.detail(id), device);
      await queryClient.invalidateQueries({ queryKey: deviceKeys.lists() });
      // The walk's own copy has not moved, but which fields it disagrees with the device about
      // may have — and that is what the fingerprint tab renders.
      await queryClient.invalidateQueries({ queryKey: deviceKeys.fingerprint(id) });
    },
  });
}

export function useDeleteDevice() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (id: string) => {
      const { error, response } = await api.DELETE('/api/v1/devices/{id}', {
        params: { path: { id } },
      });

      if (!response.ok) {
        throw toRequestError(error, 'The device could not be removed.');
      }
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: deviceKeys.all }),
  });
}

/**
 * Replaces the whole set of profiles assigned to a device.
 *
 * Whole-set replacement rather than add and remove, which is what the API offers and why: the
 * request then says what is true afterwards, and two operators editing one device cannot
 * interleave into a set neither asked for (WP-1.2).
 */
export function useSetDeviceCredentialProfiles(id: string) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (profileIds: readonly string[]) => {
      const { data, error, response } = await api.PUT(
        '/api/v1/devices/{deviceId}/credential-profiles',
        { params: { path: { deviceId: id } }, body: { credentialProfileIds: [...profileIds] } },
      );

      if (!response.ok || data === undefined) {
        throw toRequestError(error, 'The credential profiles could not be saved.');
      }

      return data;
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: deviceKeys.credentialProfiles(id) }),
  });
}

/**
 * Turns an RFC 9457 body into something a form can render.
 *
 * `detail` is what the server chose to say and is preferred over anything written here — the
 * wording of a refusal belongs to whoever knows why (the same reason WP-0.7 renders the password
 * policy in the server's own words).
 */
function toRequestError(error: unknown, fallback: string): DeviceRequestError {
  // Read structurally rather than as the generated `ProblemDetails`: the API answers RFC 9457
  // with a `code` on every refusal (CONVENTIONS.md §4) and the OpenAPI document does not declare
  // one, so the generated type has neither `code` nor `errors`. The same reading WP-0.7's
  // change-password rejection uses, and for the same reason.
  const problem = error as
    { detail?: string; code?: string; errors?: Record<string, string[]> } | undefined;

  return new DeviceRequestError(problem?.detail ?? fallback, problem?.code, problem?.errors ?? {});
}

/**
 * The on-demand reads a person can ask for on one device, and the routes behind them.
 *
 * Five walks, five routes, one shape. They are separate routes rather than one with a parameter
 * because they are separate jobs with separate schedules and separate failure modes — a routing
 * table is nothing like a neighbour table, and an operator who wants their edges refreshed
 * should not be made to wait on, or be failed by, either of the others.
 */
export const deviceWalks = {
  fingerprint: '/api/v1/devices/{id}/walk',
  neighbors: '/api/v1/devices/{id}/neighbor-walk',
  routes: '/api/v1/devices/{id}/route-walk',
  vlans: '/api/v1/devices/{id}/vlan-walk',
  clients: '/api/v1/devices/{id}/client-walk',
} as const;

export type DeviceWalk = keyof typeof deviceWalks;

/**
 * Queues one walk of one device.
 *
 * It supersedes WP-1.5's fingerprint-only mutation rather than sitting beside it: that one did
 * the same thing for one of the five routes, and two hooks for one action is how the fingerprint
 * tab and the queue come to disagree about what a walk invalidates.
 *
 * A `409` is the API saying this device already has a `Discover` queued or running, which is a
 * real rule rather than a transient failure: two walks of one device applied in whichever order
 * they came back would have each withdrawing what the other had just established. It is carried
 * through as a `DeviceRequestError` so the screen can say so and point at the queue, where the
 * job that is in the way can now be cancelled.
 */
export function useQueueDeviceWalk(id: string) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (walk: DeviceWalk) => {
      const { data, error, response } = await api.POST(deviceWalks[walk], {
        params: { path: { id } },
      });

      if (!response.ok || data === undefined) {
        throw toRequestError(error, 'The walk could not be queued.');
      }

      return data;
    },
    onSuccess: async () => {
      // The queue, always — a walk is a job and the job list is now wrong. The fingerprint too,
      // because WP-1.5's tab reads it and a fingerprint walk is the one that rewrites it.
      await queryClient.invalidateQueries({ queryKey: deviceKeys.jobs(id) });
      await queryClient.invalidateQueries({ queryKey: deviceKeys.fingerprint(id) });
    },
  });
}

/**
 * Takes a queued job back out of the queue.
 *
 * Only a `Pending` job can be cancelled and the server is the one that says so — the row carries
 * `cancellable`, so the screen never offers a control the API would refuse. A `409` here means
 * a collector claimed the job between the page rendering and the button being pressed, which is
 * ordinary rather than exceptional on a screen that refetches every five seconds.
 */
export function useCancelDeviceJob(id: string) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (jobId: string) => {
      const { data, error, response } = await api.POST('/api/v1/devices/{id}/jobs/{jobId}/cancel', {
        params: { path: { id, jobId } },
      });

      if (!response.ok || data === undefined) {
        throw toRequestError(error, 'The job could not be cancelled.');
      }

      return data;
    },
    // Every status filter, not just the mounted one: a cancelled job leaves `Pending` and joins
    // `Cancelled`, so both lists are now wrong and only the server knows what they should say.
    onSuccess: () => queryClient.invalidateQueries({ queryKey: deviceKeys.jobs(id) }),
  });
}
