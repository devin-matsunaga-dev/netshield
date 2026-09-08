import { useInfiniteQuery, useQuery } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';
import { useState } from 'react';

import type { Schemas } from '@/api/types';
import { PageHeader } from '@/components/layout/PageHeader';
import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';
import { EmptyState } from '@/components/ui/EmptyState';
import { ErrorState } from '@/components/ui/ErrorState';
import { Select } from '@/components/ui/Select';
import { Skeleton } from '@/components/ui/Skeleton';
import { Timestamp } from '@/components/ui/Timestamp';
import { DetailList, DetailRow } from '@/features/devices/components/DetailList';
import { formatRoundTrip, requireNumber } from '@/lib/apiNumber';
import {
  discoveryRunHostsQuery,
  discoveryRunQuery,
} from '@/features/discovery/api/discoveryQueries';
import {
  hostOutcomeLabels,
  hostOutcomeTones,
  runStatusLabels,
  runStatusTones,
  runTriggerLabels,
} from '@/features/discovery/components/discoveryLabels';

/**
 * One discovery run: what was swept, what answered, and what each answer turned out to be.
 *
 * Only the addresses that answered are rows. A run over a /16 that found nothing would otherwise
 * write sixty-five thousand rows to say so — the summary above is where "was this address in
 * scope" is answered, from the ranges and the address count the run kept its own copy of.
 */
export function RunDetailPage({ runId }: { readonly runId: string }) {
  const [outcome, setOutcome] = useState<Schemas['DiscoveryHostOutcome'] | undefined>(undefined);

  const run = useQuery(discoveryRunQuery(runId));
  const hosts = useInfiniteQuery(discoveryRunHostsQuery(runId, outcome));

  if (run.isPending) {
    return (
      <>
        <PageHeader title="Discovery run" subtitle="Loading…" />
        <Card>
          <div role="status" className="space-y-3" aria-busy="true" aria-label="Loading the run">
            <Skeleton className="w-64" />
            <Skeleton className="w-48" />
          </div>
        </Card>
      </>
    );
  }

  if (run.isError) {
    return (
      <>
        <PageHeader title="Discovery run" subtitle="This run could not be loaded." />
        <Card>
          <ErrorState
            title="The discovery run could not be loaded."
            action="It may have been removed, or the API may not be reachable. Try again, or go back to discovery."
            onRetry={() => void run.refetch()}
          />
        </Card>
      </>
    );
  }

  const detail = run.data;
  const rows = hosts.data?.pages.flatMap((page) => page.items) ?? [];

  return (
    <>
      <div className="mb-gutter flex items-start justify-between gap-4">
        <div>
          <div className="flex items-center gap-3">
            <h1 className="text-page-title text-primary">{detail.seedName}</h1>
            <Badge tone={runStatusTones[detail.status]}>{runStatusLabels[detail.status]}</Badge>
          </div>
          <p className="text-page-subtitle text-secondary">
            {runTriggerLabels[detail.trigger]} run, started <Timestamp value={detail.startedAt} />.
          </p>
        </div>
        <Link to="/devices/discovery">
          <Button variant="secondary">Back to discovery</Button>
        </Link>
      </div>

      <div className="mb-gutter">
        <Card title="What was swept">
          <DetailList>
            <DetailRow label="Ranges" mono>
              {detail.ranges.join(', ')}
            </DetailRow>
            <DetailRow label="Exclusions" mono>
              {detail.exclusions.length === 0 ? undefined : detail.exclusions.join(', ')}
            </DetailRow>
            <DetailRow label="Addresses in scope">{detail.addressCount.toLocaleString()}</DetailRow>
            <DetailRow label="Responded">
              {/* The pair that says what the run actually found. */}
              {`${detail.respondedCount.toLocaleString()} of ${detail.addressCount.toLocaleString()}`}
            </DetailRow>
            <DetailRow label="Sweep jobs">
              {`${detail.jobsCompleted.toString()} of ${detail.jobCount.toString()} completed`}
              {requireNumber(detail.jobsFailed) > 0
                ? `, ${requireNumber(detail.jobsFailed).toString()} failed`
                : ''}
            </DetailRow>
            <DetailRow label="Finished">
              <Timestamp value={detail.completedAt} />
            </DetailRow>
            <DetailRow label="New candidates">
              {detail.newCandidateCount.toLocaleString()}
            </DetailRow>
            <DetailRow label="Already known">
              {`${detail.knownCandidateCount.toLocaleString()} candidates, ${detail.existingDeviceCount.toLocaleString()} devices, ${detail.ignoredCount.toLocaleString()} ignored`}
            </DetailRow>
          </DetailList>

          {/*
            A run that swept nine spans and failed the tenth found something real and also missed
            a tenth of the range. Saying so is the whole reason the status has three terminal
            members rather than two.
          */}
          {detail.status === 'PartiallyFailed' && requireNumber(detail.jobsFailed) > 0 && (
            <p role="status" className="mt-gutter text-metric-caption text-warning">
              {requireNumber(detail.jobsFailed)} of {requireNumber(detail.jobCount)} sweep jobs
              failed, so part of these ranges was never probed. What is listed below is real; what
              is missing may not be.
            </p>
          )}
        </Card>
      </div>

      <Card title="Hosts that answered">
        <div className="mb-gutter flex items-end justify-between gap-4">
          <div className="w-56">
            <Select
              label="Outcome"
              anyLabel="Any outcome"
              value={outcome ?? ''}
              onChange={(event) => {
                setOutcome(
                  event.target.value === ''
                    ? undefined
                    : (event.target.value as Schemas['DiscoveryHostOutcome']),
                );
              }}
              options={(
                ['NewCandidate', 'KnownCandidate', 'ExistingDevice', 'Ignored'] as const
              ).map((value) => ({ value, label: hostOutcomeLabels[value] }))}
            />
          </div>
          {hosts.hasNextPage && (
            <Button
              variant="secondary"
              disabled={hosts.isFetchingNextPage}
              onClick={() => void hosts.fetchNextPage()}
            >
              Load more
            </Button>
          )}
        </div>

        {hosts.isPending ? (
          <div role="status" className="space-y-3" aria-busy="true" aria-label="Loading hosts">
            {Array.from({ length: 5 }, (_, index) => (
              <Skeleton key={index} className="h-8 w-full" />
            ))}
          </div>
        ) : hosts.isError ? (
          <ErrorState
            title="The hosts this run found could not be loaded."
            action="NetShield could not reach the API. Check that it is running and try again."
            onRetry={() => void hosts.refetch()}
          />
        ) : rows.length === 0 ? (
          <EmptyState
            title={
              outcome === undefined ? 'Nothing answered this sweep.' : 'No hosts with this outcome.'
            }
            action="Only addresses that replied are recorded. Silence is not a row."
          />
        ) : (
          <div role="table" aria-label="Hosts that answered" className="flex flex-col">
            <div
              role="row"
              className="flex items-center gap-4 border-b border-subtle py-2 text-table-header text-muted"
            >
              <span role="columnheader" className="w-44 shrink-0">
                Address
              </span>
              <span role="columnheader" className="w-44 shrink-0">
                Outcome
              </span>
              <span role="columnheader" className="w-28 shrink-0">
                Round trip
              </span>
              <span role="columnheader" className="w-32 shrink-0">
                Observed
              </span>
              <span role="columnheader" className="flex-1">
                <span className="sr-only">Device</span>
              </span>
            </div>

            {rows.map((host) => (
              <div
                key={host.id}
                role="row"
                className="flex h-row items-center gap-4 border-b border-subtle text-table-cell text-secondary hover:bg-raised"
              >
                <span role="cell" className="w-44 shrink-0 font-mono text-primary tabular-nums">
                  {host.address}
                </span>
                <span role="cell" className="w-44 shrink-0">
                  <Badge tone={hostOutcomeTones[host.outcome]}>
                    {hostOutcomeLabels[host.outcome]}
                  </Badge>
                </span>
                <span role="cell" className="w-28 shrink-0 tabular-nums">
                  {formatRoundTrip(host.rttMilliseconds)}
                </span>
                <span role="cell" className="w-32 shrink-0">
                  <Timestamp value={host.observedAt} />
                </span>
                <span role="cell" className="flex-1">
                  {host.deviceId !== null && (
                    <Link
                      to="/devices/$deviceId"
                      params={{ deviceId: host.deviceId }}
                      className="rounded-control text-accent hover:underline focus-visible:ring-2 focus-visible:ring-accent focus-visible:outline-none"
                    >
                      Open device
                    </Link>
                  )}
                </span>
              </div>
            ))}
          </div>
        )}
      </Card>
    </>
  );
}
