import { useQuery } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';
import { useState } from 'react';

import type { Schemas } from '@/api/types';
import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';
import { EmptyState } from '@/components/ui/EmptyState';
import { ErrorState } from '@/components/ui/ErrorState';
import { Skeleton } from '@/components/ui/Skeleton';
import { useToast } from '@/lib/toast';
import {
  credentialProfileListQuery,
  deviceCredentialProfilesQuery,
} from '@/features/devices/api/deviceQueries';
import { useSetDeviceCredentialProfiles } from '@/features/devices/api/deviceMutations';

/** What each credential kind is called on screen. Sentence case, like every other label. */
const kindLabels: Record<Schemas['CredentialKind'], string> = {
  SnmpV2c: 'SNMP v2c',
  SnmpV3: 'SNMP v3',
  SshPassword: 'SSH password',
  SshKey: 'SSH key',
};

/**
 * Which credential profiles this device may be reached with.
 *
 * Whole-set replacement rather than add and remove, which is the shape the API offers and why:
 * the request says what is true afterwards, so two operators editing one device cannot
 * interleave into a set neither asked for.
 *
 * Nothing here shows a secret, and nothing could — the API never returns one in any response
 * shape, and a structural test over the OpenAPI document fails the build if a shape ever
 * acquires one (WP-1.2). What is listed is a profile's name, kind and username, which is why the
 * whole tab is behind `CredentialsManage` rather than `InventoryRead`.
 */
export function DeviceCredentialsTab({ deviceId }: { readonly deviceId: string }) {
  const assigned = useQuery(deviceCredentialProfilesQuery(deviceId));
  const available = useQuery(credentialProfileListQuery());
  const save = useSetDeviceCredentialProfiles(deviceId);
  const toast = useToast();

  const [editing, setEditing] = useState<readonly string[] | null>(null);

  if (assigned.isPending || available.isPending) {
    return (
      <Card title="Credential profiles">
        <div
          role="status"
          className="space-y-3"
          aria-busy="true"
          aria-label="Loading credential profiles"
        >
          <Skeleton className="w-56" />
          <Skeleton className="w-40" />
        </div>
      </Card>
    );
  }

  if (assigned.isError || available.isError) {
    return (
      <Card title="Credential profiles">
        <ErrorState
          title="The credential profiles could not be loaded."
          action="NetShield could not reach the API. Check that it is running and try again."
          onRetry={() => {
            void assigned.refetch();
            void available.refetch();
          }}
        />
      </Card>
    );
  }

  const current = assigned.data;
  const all = available.data;
  const selection = editing ?? current.map((profile) => profile.id);

  function toggle(id: string) {
    setEditing(
      selection.includes(id)
        ? selection.filter((candidate) => candidate !== id)
        : [...selection, id],
    );
  }

  if (all.length === 0) {
    return (
      <Card title="Credential profiles">
        <EmptyState
          title="No credential profiles exist yet."
          action="NetShield needs a credential to walk or poll anything. Create one, then come back and assign it."
        >
          <Link to="/devices/credentials">
            <Button>Add a credential profile</Button>
          </Link>
        </EmptyState>
      </Card>
    );
  }

  return (
    <Card title="Credential profiles">
      <p className="mb-gutter text-body text-secondary">
        Which credentials NetShield may reach this device with. Secrets are never shown and never
        leave the API — only the profile is chosen here.
      </p>

      <ul className="space-y-2">
        {all.map((profile) => {
          const checked = selection.includes(profile.id);

          return (
            <li key={profile.id}>
              <label className="flex cursor-pointer items-center gap-3 rounded-control border border-subtle px-3 py-2 transition-colors duration-hover hover:bg-raised">
                <input
                  type="checkbox"
                  checked={checked}
                  onChange={() => {
                    toggle(profile.id);
                  }}
                  className="h-4 w-4 accent-accent focus-visible:ring-2 focus-visible:ring-accent focus-visible:outline-none"
                />
                <span className="flex-1 text-body text-primary">{profile.name}</span>
                {profile.username !== null && (
                  <span className="font-mono text-table-cell text-muted">{profile.username}</span>
                )}
                <Badge tone="accent">{kindLabels[profile.kind]}</Badge>
              </label>
            </li>
          );
        })}
      </ul>

      {editing !== null && (
        <div className="mt-gutter flex justify-end gap-2">
          <Button
            variant="ghost"
            onClick={() => {
              setEditing(null);
            }}
          >
            Cancel
          </Button>
          <Button
            disabled={save.isPending}
            onClick={() => {
              save.mutate(selection, {
                onSuccess: () => {
                  setEditing(null);
                  toast.announce('Credential profiles saved');
                },
                onError: (error) => {
                  toast.announce(error.message, 'danger');
                },
              });
            }}
          >
            Save assignment
          </Button>
        </div>
      )}
    </Card>
  );
}
