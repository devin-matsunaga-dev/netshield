import { useInfiniteQuery } from '@tanstack/react-query';
import { Link, useNavigate } from '@tanstack/react-router';
import { useCallback, useState } from 'react';

import { PageHeader } from '@/components/layout/PageHeader';
import type { Schemas } from '@/api/types';
import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';
import { ConfirmDelete } from '@/components/ui/ConfirmDelete';
import { EmptyState } from '@/components/ui/EmptyState';
import { ErrorState } from '@/components/ui/ErrorState';
import { Modal } from '@/components/ui/Modal';
import { RowMenu, RowMenuItem } from '@/components/ui/RowMenu';
import { Select } from '@/components/ui/Select';
import { Skeleton } from '@/components/ui/Skeleton';
import { TextField } from '@/components/ui/TextField';
import { Timestamp } from '@/components/ui/Timestamp';
import {
  credentialKinds,
  hasActiveFilter,
  type CredentialListFilters,
} from '@/features/credentials/api/credentialFilters';
import {
  credentialListQuery,
  type CredentialProfileSummary,
} from '@/features/credentials/api/credentialQueries';
import {
  useCreateCredentialProfile,
  useDeleteCredentialProfile,
  useRotateCredentialMaterial,
  useUpdateCredentialProfile,
  type CredentialRequestError,
} from '@/features/credentials/api/credentialMutations';
import {
  CredentialForm,
  type CredentialFormValues,
} from '@/features/credentials/components/CredentialForm';
import { kindLabels } from '@/features/credentials/components/credentialLabels';
import { RotateMaterialDialog } from '@/features/credentials/components/RotateMaterialDialog';
import { toNumber } from '@/lib/apiNumber';
import { useToast } from '@/lib/toast';

interface CredentialListPageProps {
  readonly filters: CredentialListFilters;
}

/** Which dialog is open, if any. One at a time — they all act on the same list. */
type Dialog =
  | { kind: 'create' }
  | { kind: 'edit'; profile: CredentialProfileSummary }
  | { kind: 'rotate'; profile: CredentialProfileSummary }
  | { kind: 'delete'; profile: CredentialProfileSummary };

/**
 * The credential profiles screen.
 *
 * The whole of it is behind `CredentialsManage`, which only an Administrator holds. WP-1.2
 * settled that even reading the list is: a profile's username is half of an SSH credential, and
 * the list of names says which accounts NetShield holds passwords for.
 *
 * **Nothing here shows a secret and nothing here could.** The API returns no material in any
 * response shape, and `ApiSecretExposureTests` fails the build if one ever appears — so what is
 * listed is a name, a kind, a username, how many devices use it and when it last changed. A
 * secret exists on this side only inside a create or rotate form, and only until the request
 * returns.
 *
 * At `/devices/credentials` with no sidebar entry, for the reason the discovery screen is at
 * `/devices/discovery`: the sidebar is `docs/design/reference-dashboard.png`'s, and DESIGN.md §9
 * admits no invented visual direction.
 */
export function CredentialListPage({ filters }: CredentialListPageProps) {
  const navigate = useNavigate();
  const toast = useToast();

  const profiles = useInfiniteQuery(credentialListQuery(filters));
  const [dialog, setDialog] = useState<Dialog | null>(null);

  const create = useCreateCredentialProfile();
  const remove = useDeleteCredentialProfile();

  const applyFilter = useCallback(
    (change: Partial<CredentialListFilters>) => {
      void navigate({
        to: '/devices/credentials',
        search: (current) => ({ ...current, ...change }),
        replace: true,
      });
    },
    [navigate],
  );

  const clearFilters = useCallback(() => {
    void navigate({ to: '/devices/credentials', search: {}, replace: true });
  }, [navigate]);

  const rows = profiles.data?.pages.flatMap((page) => page.items) ?? [];
  const total = toNumber(profiles.data?.pages[0]?.totalCount);

  return (
    <>
      <div className="mb-gutter flex items-start justify-between gap-4">
        <PageHeader
          title="Credential profiles"
          subtitle={
            total === null
              ? 'The credentials NetShield may reach devices with. Secrets are never shown.'
              : `${total.toString()} ${total === 1 ? 'profile' : 'profiles'}. Secrets are never shown.`
          }
        />
        <div className="flex gap-2">
          <Link to="/devices">
            <Button variant="secondary">Back to devices</Button>
          </Link>
          <Button
            onClick={() => {
              setDialog({ kind: 'create' });
            }}
          >
            Add profile
          </Button>
        </div>
      </div>

      <div className="mb-gutter">
        <Card>
          <div className="flex flex-wrap items-end gap-4">
            <div className="w-72">
              <TextField
                label="Search"
                type="search"
                placeholder="Profile name"
                value={filters.search ?? ''}
                onChange={(event) => {
                  applyFilter({
                    search: event.target.value === '' ? undefined : event.target.value,
                  });
                }}
              />
            </div>
            <div className="w-52">
              <Select
                label="Kind"
                anyLabel="Any kind"
                value={filters.kind ?? ''}
                onChange={(event) => {
                  applyFilter({
                    kind:
                      event.target.value === ''
                        ? undefined
                        : (event.target.value as Schemas['CredentialKind']),
                  });
                }}
                options={credentialKinds.map((kind) => ({ value: kind, label: kindLabels[kind] }))}
              />
            </div>
            {hasActiveFilter(filters) && (
              <Button variant="ghost" onClick={clearFilters}>
                Clear filters
              </Button>
            )}
          </div>
        </Card>
      </div>

      <Card>
        {profiles.isPending ? (
          <div role="status" className="space-y-3" aria-busy="true" aria-label="Loading profiles">
            {Array.from({ length: 5 }, (_, index) => (
              <Skeleton key={index} className="h-8 w-full" />
            ))}
          </div>
        ) : profiles.isError ? (
          <ErrorState
            title="The credential profiles could not be loaded."
            action="NetShield could not reach the API. Check that it is running and try again."
            onRetry={() => void profiles.refetch()}
          />
        ) : rows.length === 0 ? (
          hasActiveFilter(filters) ? (
            <EmptyState
              title="No profiles match these filters."
              action="Widen or clear the filters to see the rest."
            >
              <Button variant="secondary" onClick={clearFilters}>
                Clear filters
              </Button>
            </EmptyState>
          ) : (
            <EmptyState
              title="No credential profiles yet."
              action="NetShield needs a credential before it can walk or poll anything. Add one, then assign it on a device's Credentials tab."
            >
              <Button
                onClick={() => {
                  setDialog({ kind: 'create' });
                }}
              >
                Add profile
              </Button>
            </EmptyState>
          )
        ) : (
          <CredentialTable rows={rows} onAct={setDialog} />
        )}
      </Card>

      {dialog?.kind === 'create' && (
        <Modal
          title="Add a credential profile"
          onClose={() => {
            setDialog(null);
          }}
        >
          <CredentialForm
            kindEditable
            withSecrets
            submitLabel="Add profile"
            pending={create.isPending}
            error={(create.error as CredentialRequestError | null) ?? null}
            onCancel={() => {
              setDialog(null);
            }}
            onSubmit={(values) => {
              create.mutate(toCreateRequest(values), {
                onSuccess: () => {
                  setDialog(null);
                  create.reset();
                  toast.announce('Profile added');
                },
              });
            }}
          />
        </Modal>
      )}

      {dialog?.kind === 'edit' && (
        <EditDialog
          profile={dialog.profile}
          onClose={() => {
            setDialog(null);
          }}
          onSaved={() => {
            setDialog(null);
            toast.announce('Profile saved');
          }}
        />
      )}

      {dialog?.kind === 'rotate' && (
        <RotateDialog
          profile={dialog.profile}
          onClose={() => {
            setDialog(null);
          }}
          onSaved={() => {
            setDialog(null);
            toast.announce('Credential replaced');
          }}
        />
      )}

      {dialog?.kind === 'delete' && (
        <ConfirmDelete
          title={`Remove ${dialog.profile.name}`}
          description={describeRemoval(dialog.profile)}
          confirmWord={dialog.profile.name}
          actionLabel="Remove profile"
          pending={remove.isPending}
          onCancel={() => {
            setDialog(null);
          }}
          onConfirm={() => {
            remove.mutate(dialog.profile.id, {
              onSuccess: () => {
                setDialog(null);
                toast.announce('Profile removed');
              },
              onError: (error) => {
                toast.announce(error.message, 'danger');
              },
            });
          }}
        />
      )}
    </>
  );
}

/**
 * What removing this profile will do, said before it is done.
 *
 * The device count is the part that matters: deleting hard-deletes every assignment, so a device
 * reached only with this credential stops being reachable at all until another is assigned.
 */
function describeRemoval(profile: CredentialProfileSummary): string {
  const count = toNumber(profile.deviceCount) ?? 0;

  return count === 0
    ? 'No device uses this profile. The stored credential becomes unreachable and the profile stops being offered.'
    : `${count.toString()} ${count === 1 ? 'device is' : 'devices are'} assigned this profile and will lose it. ` +
        'Any of them reached only with this credential cannot be walked or polled until another is assigned.';
}

/** The profile table (DESIGN.md §6). Not virtualized: profiles number in the dozens, not the thousands. */
function CredentialTable({
  rows,
  onAct,
}: {
  readonly rows: readonly CredentialProfileSummary[];
  readonly onAct: (dialog: Dialog) => void;
}) {
  return (
    <table className="w-full text-table-cell">
      <caption className="sr-only">Credential profiles NetShield may reach devices with</caption>
      <thead>
        <tr className="border-b border-subtle text-left text-table-header text-muted">
          <th scope="col" className="py-2 font-medium">
            Name
          </th>
          <th scope="col" className="py-2 font-medium">
            Kind
          </th>
          <th scope="col" className="py-2 font-medium">
            Username
          </th>
          <th scope="col" className="py-2 font-medium">
            Devices
          </th>
          <th scope="col" className="py-2 font-medium">
            Secret changed
          </th>
          <th scope="col" className="w-row-menu py-2 font-medium">
            <span className="sr-only">Actions</span>
          </th>
        </tr>
      </thead>
      <tbody>
        {rows.map((profile) => (
          <tr key={profile.id} className="border-b border-subtle text-secondary last:border-0">
            <td className="py-2 text-primary">{profile.name}</td>
            <td className="py-2">
              <Badge tone="accent">{kindLabels[profile.kind]}</Badge>
            </td>
            <td className="py-2 font-mono">{profile.username ?? '—'}</td>
            <td className="py-2 tabular-nums">{String(profile.deviceCount)}</td>
            <td className="py-2 tabular-nums">
              <Timestamp value={profile.materialUpdatedAt} />
            </td>
            <td className="py-2">
              <RowMenu label={`Actions for ${profile.name}`}>
                <RowMenuItem
                  onSelect={() => {
                    onAct({ kind: 'edit', profile });
                  }}
                >
                  Edit profile
                </RowMenuItem>
                <RowMenuItem
                  onSelect={() => {
                    onAct({ kind: 'rotate', profile });
                  }}
                >
                  Replace credential
                </RowMenuItem>
                <RowMenuItem
                  onSelect={() => {
                    onAct({ kind: 'delete', profile });
                  }}
                >
                  Remove profile
                </RowMenuItem>
              </RowMenu>
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

/** The edit dialog, which owns its own mutation so the hook is bound to one profile's id. */
function EditDialog({
  profile,
  onClose,
  onSaved,
}: {
  readonly profile: CredentialProfileSummary;
  readonly onClose: () => void;
  readonly onSaved: () => void;
}) {
  const update = useUpdateCredentialProfile(profile.id);

  return (
    <Modal title={`Edit ${profile.name}`} onClose={onClose}>
      <CredentialForm
        kindEditable={false}
        withSecrets={false}
        submitLabel="Save profile"
        pending={update.isPending}
        error={(update.error as CredentialRequestError | null) ?? null}
        initial={{
          name: profile.name,
          kind: profile.kind,
          username: profile.username ?? '',
        }}
        onCancel={onClose}
        onSubmit={(values) => {
          update.mutate(
            {
              name: values.name,
              description: values.description === '' ? null : values.description,
              username: values.username === '' ? null : values.username,
              ...(profile.kind === 'SnmpV3'
                ? { authAlgorithm: values.authAlgorithm, privacyAlgorithm: values.privacyAlgorithm }
                : {}),
            },
            { onSuccess: onSaved },
          );
        }}
      />
    </Modal>
  );
}

/** The rotation dialog, which owns its own mutation for the same reason. */
function RotateDialog({
  profile,
  onClose,
  onSaved,
}: {
  readonly profile: CredentialProfileSummary;
  readonly onClose: () => void;
  readonly onSaved: () => void;
}) {
  const rotate = useRotateCredentialMaterial(profile.id);

  return (
    <RotateMaterialDialog
      profile={profile}
      // The list carries no algorithm, and a rotation does not change one. AES-128 is the shape
      // the dialog opens in for a v3 profile; the reader confirms which the profile actually has,
      // and the server refuses a member that does not belong to it either way.
      privacyAlgorithm="Aes128"
      pending={rotate.isPending}
      error={(rotate.error as CredentialRequestError | null) ?? null}
      onCancel={onClose}
      onConfirm={(material) => {
        rotate.mutate(material, { onSuccess: onSaved });
      }}
    />
  );
}

/** The form's values as the create request. Empty text is `null`, not an empty string. */
function toCreateRequest(values: CredentialFormValues) {
  return {
    name: values.name,
    kind: values.kind,
    material: values.material,
    description: values.description === '' ? null : values.description,
    username: values.username === '' ? null : values.username,
    ...(values.kind === 'SnmpV3'
      ? { authAlgorithm: values.authAlgorithm, privacyAlgorithm: values.privacyAlgorithm }
      : {}),
  };
}
