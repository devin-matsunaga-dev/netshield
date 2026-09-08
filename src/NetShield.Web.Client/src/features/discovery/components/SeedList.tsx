import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';

import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';
import { ConfirmDelete } from '@/components/ui/ConfirmDelete';
import { EmptyState } from '@/components/ui/EmptyState';
import { ErrorState } from '@/components/ui/ErrorState';
import { Skeleton } from '@/components/ui/Skeleton';
import { Timestamp } from '@/components/ui/Timestamp';
import { useToast } from '@/lib/toast';
import type { DeviceRequestError } from '@/features/devices/api/deviceMutations';
import {
  useCreateSeed,
  useDeleteSeed,
  useStartDiscoveryRun,
  useUpdateSeed,
} from '@/features/discovery/api/discoveryMutations';
import {
  discoverySeedQuery,
  discoverySeedsQuery,
  type DiscoverySeedDetail,
  type DiscoverySeedSummary,
} from '@/features/discovery/api/discoveryQueries';
import { SeedForm, type SeedFormValues } from '@/features/discovery/components/SeedForm';
import { requireNumber } from '@/lib/apiNumber';
import { RequirePermission } from '@/features/session/components/RequirePermission';

/**
 * The ranges discovery sweeps, and the control that sweeps one now.
 *
 * Editing a seed is behind `PoliciesWrite` — that permission's own definition names discovery
 * schedules — while reading one is behind `InventoryRead`, because a seed's ranges are a
 * statement about the estate that anybody who can see the device list can already see. Running
 * one is `DiscoveryRun`: making NetShield reach into the address space outside its schedule is a
 * different privilege from describing where it should look.
 *
 * SPEC.md §2 puts discovery schedules under Policies, and the richer scheduling controls belong
 * there when that screen is built. What is here is the minimum that makes discovery usable at
 * all: a fresh installation with no seed can sweep nothing, and a discovery screen that can run
 * but cannot define a target is a dead end.
 */
export function SeedList() {
  const queryClient = useQueryClient();
  const seeds = useQuery(discoverySeedsQuery());
  const create = useCreateSeed();
  const update = useUpdateSeed();
  const remove = useDeleteSeed();
  const run = useStartDiscoveryRun();
  const toast = useToast();

  const [adding, setAdding] = useState(false);
  const [editing, setEditing] = useState<DiscoverySeedDetail | null>(null);
  const [deleting, setDeleting] = useState<DiscoverySeedSummary | null>(null);

  async function openEditor(seed: DiscoverySeedSummary) {
    // The summary carries counts; the form needs the ranges themselves, which only the detail
    // route returns.
    const detail = await queryClient.query(discoverySeedQuery(seed.id));

    setEditing(detail);
  }

  function save(values: SeedFormValues) {
    const request = {
      name: values.name,
      description: values.description,
      enabled: values.enabled,
      ranges: [...values.ranges],
      exclusions: [...values.exclusions],
      intervalMinutes: values.intervalMinutes,
    };

    if (editing !== null) {
      update.mutate(
        { id: editing.id, request },
        {
          onSuccess: () => {
            setEditing(null);
            toast.announce('Seed saved');
          },
          onError: (error) => {
            toast.announce(error.message, 'danger');
          },
        },
      );

      return;
    }

    create.mutate(request, {
      onSuccess: () => {
        setAdding(false);
        toast.announce('Seed added');
      },
      onError: (error) => {
        toast.announce(error.message, 'danger');
      },
    });
  }

  const rows = seeds.data ?? [];

  return (
    <Card title="Seeds">
      <div className="mb-gutter flex items-center justify-between gap-4">
        <p className="text-body text-secondary">
          The ranges NetShield sweeps, and how often. Each sweep sends ICMP echo requests and
          nothing else.
        </p>
        <RequirePermission permission="PoliciesWrite">
          <Button
            onClick={() => {
              setAdding(true);
            }}
          >
            Add seed
          </Button>
        </RequirePermission>
      </div>

      {seeds.isPending ? (
        <div role="status" className="space-y-3" aria-busy="true" aria-label="Loading seeds">
          {Array.from({ length: 3 }, (_, index) => (
            <Skeleton key={index} className="h-8 w-full" />
          ))}
        </div>
      ) : seeds.isError ? (
        <ErrorState
          title="The discovery seeds could not be loaded."
          action="NetShield could not reach the API. Check that it is running and try again."
          onRetry={() => void seeds.refetch()}
        />
      ) : rows.length === 0 ? (
        <EmptyState
          title="No discovery seeds yet."
          action="Add the CIDR ranges NetShield should sweep. Nothing is discovered until one exists."
        >
          <RequirePermission permission="PoliciesWrite">
            <Button
              onClick={() => {
                setAdding(true);
              }}
            >
              Add seed
            </Button>
          </RequirePermission>
        </EmptyState>
      ) : (
        <div role="table" aria-label="Discovery seeds" className="flex flex-col">
          <div
            role="row"
            className="flex items-center gap-4 border-b border-subtle py-2 text-table-header text-muted"
          >
            <span role="columnheader" className="w-48 shrink-0">
              Name
            </span>
            <span role="columnheader" className="w-28 shrink-0">
              Schedule
            </span>
            <span role="columnheader" className="w-24 shrink-0">
              Ranges
            </span>
            <span role="columnheader" className="w-28 shrink-0">
              Addresses
            </span>
            <span role="columnheader" className="w-32 shrink-0">
              Last run
            </span>
            <span role="columnheader" className="w-32 shrink-0">
              Next run
            </span>
            <span role="columnheader" className="flex-1">
              <span className="sr-only">Actions</span>
            </span>
          </div>

          {rows.map((seed) => (
            <div
              key={seed.id}
              role="row"
              className="flex h-row items-center gap-4 border-b border-subtle text-table-cell text-secondary hover:bg-raised"
            >
              <span role="cell" className="w-48 shrink-0 truncate text-primary">
                {seed.name}
              </span>
              <span role="cell" className="w-28 shrink-0">
                {seed.enabled ? (
                  <Badge tone="success">
                    Every {formatInterval(requireNumber(seed.intervalMinutes))}
                  </Badge>
                ) : (
                  <Badge tone="muted">Paused</Badge>
                )}
              </span>
              <span role="cell" className="w-24 shrink-0 tabular-nums">
                {seed.rangeCount}
              </span>
              <span role="cell" className="w-28 shrink-0 tabular-nums">
                {seed.addressCount.toLocaleString()}
              </span>
              <span role="cell" className="w-32 shrink-0">
                <Timestamp value={seed.lastRunAt} />
              </span>
              <span role="cell" className="w-32 shrink-0">
                <Timestamp value={seed.nextRunAt} />
              </span>
              <span role="cell" className="flex flex-1 justify-end gap-2">
                <RequirePermission permission="DiscoveryRun">
                  <Button
                    variant="secondary"
                    disabled={run.isPending}
                    onClick={() => {
                      run.mutate(seed.id, {
                        onSuccess: (queued) => {
                          toast.announce(
                            `Sweeping ${queued.addressCount.toLocaleString()} addresses in ${queued.jobCount.toString()} jobs`,
                          );
                        },
                        onError: (error) => {
                          toast.announce(error.message, 'danger');
                        },
                      });
                    }}
                  >
                    Run now
                  </Button>
                </RequirePermission>
                <RequirePermission permission="PoliciesWrite">
                  <Button variant="ghost" onClick={() => void openEditor(seed)}>
                    Edit
                  </Button>
                  <Button
                    variant="ghost"
                    onClick={() => {
                      setDeleting(seed);
                    }}
                  >
                    Remove
                  </Button>
                </RequirePermission>
              </span>
            </div>
          ))}
        </div>
      )}

      {(adding || editing !== null) && (
        <SeedForm
          {...(editing !== null ? { seed: editing } : {})}
          pending={create.isPending || update.isPending}
          error={
            ((editing !== null ? update.error : create.error) as DeviceRequestError | null) ?? null
          }
          onSubmit={save}
          onCancel={() => {
            setAdding(false);
            setEditing(null);
          }}
        />
      )}

      {deleting !== null && (
        <ConfirmDelete
          title="Remove seed"
          description={`This stops NetShield sweeping ${deleting.name}'s ranges. Runs already recorded keep their own copy of what they swept, so the history stays readable.`}
          confirmWord={deleting.name}
          actionLabel="Remove seed"
          pending={remove.isPending}
          onCancel={() => {
            setDeleting(null);
          }}
          onConfirm={() => {
            remove.mutate(deleting.id, {
              onSuccess: () => {
                setDeleting(null);
                toast.announce('Seed removed');
              },
              onError: (error) => {
                setDeleting(null);
                toast.announce(error.message, 'danger');
              },
            });
          }}
        />
      )}
    </Card>
  );
}

/** "6 hours", "1 day" — the interval as a person would say it rather than in minutes. */
function formatInterval(minutes: number): string {
  if (minutes % 1440 === 0) {
    const days = minutes / 1440;

    return `${days.toString()} ${days === 1 ? 'day' : 'days'}`;
  }

  if (minutes % 60 === 0) {
    const hours = minutes / 60;

    return `${hours.toString()} ${hours === 1 ? 'hour' : 'hours'}`;
  }

  return `${minutes.toString()} min`;
}
