import { useNavigate } from '@tanstack/react-router';
import { useState } from 'react';

import { Card } from '@/components/ui/Card';
import { ConfirmDelete } from '@/components/ui/ConfirmDelete';
import { useToast } from '@/lib/toast';
import type { DeviceDetail } from '@/features/devices/api/deviceQueries';
import {
  useDeleteDevice,
  useUpdateDevice,
  type DeviceRequestError,
} from '@/features/devices/api/deviceMutations';
import { DeviceForm, type DeviceFormValues } from '@/features/devices/components/DeviceForm';
import { fromDetail, toRequest } from '@/features/devices/components/deviceFormValues';

/**
 * Editing and removing a device.
 *
 * Only drawn for a session holding `InventoryWrite` — the caller decides that, so a read-only
 * user never meets a form whose Save button the API would refuse.
 *
 * Removal is a soft delete: the row stays so that telemetry and audit rows naming it still
 * resolve, and the address is released for the replacement being racked. That is worth saying on
 * the screen, because "delete" reads as "gone" and this one is not.
 */
export function DeviceSettingsTab({ device }: { readonly device: DeviceDetail }) {
  const navigate = useNavigate();
  const toast = useToast();
  const update = useUpdateDevice(device.id);
  const remove = useDeleteDevice();
  const [confirming, setConfirming] = useState(false);

  function save(values: DeviceFormValues) {
    update.mutate(toRequest(values), {
      onSuccess: () => {
        toast.announce('Device saved');
      },
      onError: (error) => {
        toast.announce(error.message, 'danger');
      },
    });
  }

  return (
    <div className="space-y-gutter">
      <Card title="Edit device">
        <DeviceForm
          initial={fromDetail(device)}
          submitLabel="Save device"
          pending={update.isPending}
          error={(update.error as DeviceRequestError | null) ?? null}
          onSubmit={save}
          onCancel={() => void navigate({ to: '/devices' })}
        />
      </Card>

      <Card title="Remove device">
        <p className="mb-gutter text-body text-secondary">
          Removing {device.hostname} takes it out of the inventory and stops NetShield polling it.
          Its history is kept so that alerts and audit rows naming it still resolve, and its address
          is released for whatever replaces it.
        </p>
        <button
          type="button"
          onClick={() => {
            setConfirming(true);
          }}
          className="inline-flex h-control items-center justify-center rounded-control bg-danger px-4 text-nav-item text-white transition-colors duration-hover hover:opacity-90 focus-visible:ring-2 focus-visible:ring-accent focus-visible:outline-none"
        >
          Remove device
        </button>
      </Card>

      {confirming && (
        <ConfirmDelete
          title="Remove device"
          description={`This takes ${device.hostname} out of the inventory and stops every scheduled probe and walk of it.`}
          confirmWord={device.hostname}
          actionLabel="Remove device"
          pending={remove.isPending}
          onCancel={() => {
            setConfirming(false);
          }}
          onConfirm={() => {
            remove.mutate(device.id, {
              onSuccess: () => {
                toast.announce('Device removed');
                void navigate({ to: '/devices' });
              },
              onError: (error) => {
                setConfirming(false);
                toast.announce(error.message, 'danger');
              },
            });
          }}
        />
      )}
    </div>
  );
}
