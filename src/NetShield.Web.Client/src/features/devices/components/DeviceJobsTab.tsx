import { useInfiniteQuery } from '@tanstack/react-query';
import { useVirtualizer } from '@tanstack/react-virtual';
import { useCallback, useRef, useState } from 'react';

import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';
import { EmptyState } from '@/components/ui/EmptyState';
import { ErrorState } from '@/components/ui/ErrorState';
import { Select, type SelectOption } from '@/components/ui/Select';
import { Skeleton } from '@/components/ui/Skeleton';
import { Timestamp } from '@/components/ui/Timestamp';
import {
  useCancelDeviceJob,
  useQueueDeviceWalk,
  type DeviceWalk,
} from '@/features/devices/api/deviceMutations';
import { deviceJobsQuery, type CollectorJobStatus } from '@/features/devices/api/deviceQueries';
import {
  describeJob,
  jobStatusLabels,
  jobStatusTones,
  walkActions,
} from '@/features/devices/components/jobLabels';
import { RequirePermission } from '@/features/session/components/RequirePermission';
import { requireNumber } from '@/lib/apiNumber';
import { useToast } from '@/lib/toast';

/** DESIGN.md §6: rows are 44px. */
const rowHeight = 44;

/** The statuses the filter offers, in the order a job passes through them. */
const statusOptions: readonly SelectOption[] = (
  ['Pending', 'Leased', 'Succeeded', 'Failed', 'Cancelled'] as const
).map((status) => ({ value: status, label: jobStatusLabels[status] }));

/**
 * What NetShield has asked the collector to do for this device.
 *
 * **The screen exists because "I pressed walk — did anything happen?" had no answer.** A walk
 * answers `202` with a job id and then nothing visible happens for as long as it takes a
 * collector to lease it; if no collector ever does — because it cannot reach the API, because
 * the device has no credential, because the job is stuck — the device simply never updates and
 * the product says nothing. Now the queue says it.
 *
 * **And it is the way out of the deadlock that causes.** One outstanding `Discover` per device
 * is refused with a `409`, so a job that will never run blocks every future walk of that device
 * for ever. Cancelling is what clears it.
 *
 * It refetches on a five-second interval rather than offering a refresh button: watching a job
 * move from queued to running to finished is the whole interaction.
 */
export function DeviceJobsTab({ deviceId }: { readonly deviceId: string }) {
  const [status, setStatus] = useState<CollectorJobStatus | undefined>(undefined);
  const scroller = useRef<HTMLDivElement>(null);
  const toast = useToast();

  const jobs = useInfiniteQuery(deviceJobsQuery(deviceId, status));
  const walk = useQueueDeviceWalk(deviceId);
  const cancel = useCancelDeviceJob(deviceId);

  const rows = jobs.data?.pages.flatMap((page) => page.items) ?? [];

  const virtualizer = useVirtualizer({
    count: rows.length,
    getScrollElement: () => scroller.current,
    estimateSize: () => rowHeight,
    overscan: 12,
  });

  const queueWalk = useCallback(
    (kind: DeviceWalk, label: string) => {
      walk.mutate(kind, {
        onSuccess: () => {
          toast.announce(`${label} walk queued.`);
        },
        onError: (error) => {
          // A 409 is the outstanding-walk rule rather than a failure, and the queue below is
          // where the reader can do something about it, so the message points at it.
          toast.announce(
            error.message.length > 0
              ? error.message
              : 'The walk could not be queued. Check the queue below for a job already waiting.',
          );
        },
      });
    },
    [walk, toast],
  );

  const cancelJob = useCallback(
    (jobId: string) => {
      cancel.mutate(jobId, {
        onSuccess: () => {
          toast.announce('Job cancelled.');
        },
        onError: (error) => {
          toast.announce(
            error.message.length > 0
              ? error.message
              : 'The job could not be cancelled. A collector may have started it.',
          );
        },
      });
    },
    [cancel, toast],
  );

  return (
    <div className="flex flex-col gap-gutter">
      <Card title="Run a walk now">
        <div className="flex flex-wrap items-center gap-2">
          {/*
            Hidden for a session that cannot run one, and refused by the API either way — hiding
            is presentation and never the boundary (ARCHITECTURE.md §8).
          */}
          <RequirePermission permission="DiscoveryRun">
            {walkActions.map((action) => (
              <Button
                key={action.walk}
                variant="secondary"
                disabled={walk.isPending}
                onClick={() => {
                  queueWalk(action.walk, action.label);
                }}
              >
                {action.label}
              </Button>
            ))}
          </RequirePermission>
        </div>
        <p className="pt-3 text-metric-caption text-muted">
          A walk is queued for the collector rather than run here, so nothing changes until a
          collector leases it. The queue below is where that happens.
        </p>
      </Card>

      <Card
        title="Queue"
        control={
          <div className="w-44">
            <Select
              label="Status"
              options={statusOptions}
              anyLabel="Every status"
              value={status ?? ''}
              onChange={(event) => {
                setStatus(
                  event.target.value === ''
                    ? undefined
                    : (event.target.value as CollectorJobStatus),
                );
              }}
            />
          </div>
        }
      >
        {jobs.isPending ? (
          <div role="status" className="space-y-3" aria-busy="true" aria-label="Loading the queue">
            {Array.from({ length: 5 }, (_, index) => (
              <Skeleton key={index} className="h-8 w-full" />
            ))}
          </div>
        ) : jobs.isError ? (
          <ErrorState
            title="The job queue could not be loaded."
            action="NetShield could not reach the API. Check that it is running and try again."
            onRetry={() => void jobs.refetch()}
          />
        ) : rows.length === 0 ? (
          <EmptyState
            title={
              status === undefined
                ? 'Nothing has been asked of this device yet.'
                : `No ${jobStatusLabels[status].toLowerCase()} jobs.`
            }
            action={
              status === undefined
                ? 'Run a walk above, or wait for the schedule to reach it.'
                : 'Choose a different status to see the rest of the queue.'
            }
          />
        ) : (
          <div
            role="table"
            aria-label="Collector jobs"
            aria-rowcount={rows.length}
            className="flex flex-col"
          >
            <div
              role="row"
              className="flex items-center gap-4 border-b border-subtle px-gutter py-2 text-table-header text-muted"
            >
              <span role="columnheader" className="w-40 shrink-0">
                Job
              </span>
              <span role="columnheader" className="w-28 shrink-0">
                Status
              </span>
              <span role="columnheader" className="w-24 shrink-0">
                Attempts
              </span>
              <span role="columnheader" className="w-32 shrink-0">
                Requested
              </span>
              <span role="columnheader" className="w-32 shrink-0">
                Collector
              </span>
              <span role="columnheader" className="flex-1">
                Detail
              </span>
              <span role="columnheader" className="w-24 shrink-0">
                <span className="sr-only">Actions</span>
              </span>
            </div>

            <div ref={scroller} className="max-h-[50vh] overflow-auto" data-testid="job-scroller">
              <div
                className="relative w-full"
                style={{ height: `${virtualizer.getTotalSize().toString()}px` }}
              >
                {virtualizer.getVirtualItems().map((row) => {
                  const job = rows[row.index];

                  if (job === undefined) {
                    return null;
                  }

                  return (
                    <div
                      key={job.id}
                      role="row"
                      aria-rowindex={row.index + 1}
                      className="absolute top-0 left-0 flex w-full items-center gap-4 border-b border-subtle px-gutter text-table-cell text-secondary transition-colors duration-hover hover:bg-raised"
                      style={{
                        height: `${rowHeight.toString()}px`,
                        transform: `translateY(${row.start.toString()}px)`,
                      }}
                    >
                      <span role="cell" className="w-40 shrink-0 truncate text-primary">
                        {describeJob(job.kind, job.walk)}
                      </span>
                      <span role="cell" className="w-28 shrink-0">
                        <Badge tone={jobStatusTones[job.status]}>
                          {jobStatusLabels[job.status]}
                        </Badge>
                      </span>
                      <span role="cell" className="w-24 shrink-0 tabular-nums">
                        {requireNumber(job.attempts)} of {requireNumber(job.maxAttempts)}
                      </span>
                      <span role="cell" className="w-32 shrink-0 tabular-nums">
                        <Timestamp value={job.createdAt} />
                      </span>
                      <span role="cell" className="w-32 shrink-0 truncate">
                        {job.leasedBy ?? '—'}
                      </span>
                      <span role="cell" className="flex-1 truncate" title={job.detail ?? undefined}>
                        {job.detail ?? '—'}
                      </span>
                      <span role="cell" className="w-24 shrink-0">
                        {/*
                          `cancellable` is the server's answer, not the client's guess, so the
                          control is never offered where the API would refuse it.
                        */}
                        {job.cancellable && (
                          <RequirePermission permission="DiscoveryRun">
                            <Button
                              variant="ghost"
                              disabled={cancel.isPending}
                              aria-label={`Cancel ${describeJob(job.kind, job.walk)}`}
                              onClick={() => {
                                cancelJob(job.id);
                              }}
                            >
                              Cancel
                            </Button>
                          </RequirePermission>
                        )}
                      </span>
                    </div>
                  );
                })}
              </div>
            </div>

            {jobs.hasNextPage && (
              <div className="border-t border-subtle px-gutter py-2">
                <Button
                  variant="ghost"
                  disabled={jobs.isFetchingNextPage}
                  onClick={() => void jobs.fetchNextPage()}
                >
                  {jobs.isFetchingNextPage ? 'Loading…' : 'Load older jobs'}
                </Button>
              </div>
            )}
          </div>
        )}
      </Card>
    </div>
  );
}
