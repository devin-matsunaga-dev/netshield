import { useNavigate } from '@tanstack/react-router';

import { PageHeader } from '@/components/layout/PageHeader';
import { Card } from '@/components/ui/Card';
import { useToast } from '@/lib/toast';
import { useCreateDevice, type DeviceRequestError } from '@/features/devices/api/deviceMutations';
import { DeviceForm, type DeviceFormValues } from '@/features/devices/components/DeviceForm';
import { toRequest } from '@/features/devices/components/deviceFormValues';

/**
 * Adding a device by hand.
 *
 * The device starts `Unknown` in both senses: nothing has probed it, so it has no state, and
 * nothing has walked it, so its vendor is whatever the person typed. Both are corrected by the
 * schedule — fingerprinting exists to correct a guess an operator made.
 */
export function AddDevicePage() {
  const navigate = useNavigate();
  const toast = useToast();
  const create = useCreateDevice();

  function submit(values: DeviceFormValues) {
    create.mutate(toRequest(values), {
      onSuccess: (device) => {
        // The same word as the button, in the past tense (DESIGN.md §8).
        toast.announce('Device added');
        void navigate({ to: '/devices/$deviceId', params: { deviceId: device.id } });
      },
    });
  }

  return (
    <>
      <PageHeader
        title="Add device"
        subtitle="Record a device NetShield should monitor. Discovery finds the rest."
      />
      <Card>
        <DeviceForm
          submitLabel="Add device"
          pending={create.isPending}
          error={(create.error as DeviceRequestError | null) ?? null}
          onSubmit={submit}
          onCancel={() => void navigate({ to: '/devices' })}
        />
      </Card>
    </>
  );
}
