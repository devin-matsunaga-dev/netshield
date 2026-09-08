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
 * Asks the collector to fingerprint a device now. Answers 202 with a job id — nothing has been
 * walked when it returns, because ARCHITECTURE.md §7 puts device contact on the collector.
 */
export function useQueueDeviceWalk(id: string) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async () => {
      const { data, error, response } = await api.POST('/api/v1/devices/{id}/walk', {
        params: { path: { id } },
      });

      if (!response.ok || data === undefined) {
        throw toRequestError(error, 'The walk could not be queued.');
      }

      return data;
    },
    // Nothing to invalidate yet: the result arrives when a collector has leased the job, run it
    // and reported back, which is minutes rather than milliseconds. The reader refreshes the tab.
    onSuccess: () => queryClient.invalidateQueries({ queryKey: deviceKeys.fingerprint(id) }),
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
