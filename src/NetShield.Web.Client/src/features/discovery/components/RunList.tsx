import { useInfiniteQuery } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';

import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';
import { EmptyState } from '@/components/ui/EmptyState';
import { ErrorState } from '@/components/ui/ErrorState';
import { Skeleton } from '@/components/ui/Skeleton';
import { Timestamp } from '@/components/ui/Timestamp';
import { discoveryRunsQuery } from '@/features/discovery/api/discoveryQueries';
import {
  runStatusLabels,
  runStatusTones,
  runTriggerLabels,
} from '@/features/discovery/components/discoveryLabels';

/**
 * Every discovery run, newest first.
 *
 * The two numbers that matter sit beside each other: how many addresses were in scope, and how
 * many answered. A run that swept 254 and found 3 is a different fact from one that swept 254
 * and found none, and neither is visible from a status alone.
 */
export function RunList() {
  const runs = useInfiniteQuery(discoveryRunsQuery(undefined));
  const rows = runs.data?.pages.flatMap((page) => page.items) ?? [];

  return (
    <Card title="Runs">
      {runs.isPending ? (
        <div
          role="status"
          className="space-y-3"
          aria-busy="true"
          aria-label="Loading discovery runs"
        >
          {Array.from({ length: 5 }, (_, index) => (
            <Skeleton key={index} className="h-8 w-full" />
          ))}
        </div>
      ) : runs.isError ? (
        <ErrorState
          title="The discovery runs could not be loaded."
          action="NetShield could not reach the API. Check that it is running and try again."
          onRetry={() => void runs.refetch()}
        />
      ) : rows.length === 0 ? (
        <EmptyState
          title="No discovery runs yet."
          action="Add a seed with the ranges to sweep, then run it. A seed also runs on its own interval."
        />
      ) : (
        <>
          <div role="table" aria-label="Discovery runs" className="flex flex-col">
            <div
              role="row"
              className="flex items-center gap-4 border-b border-subtle py-2 text-table-header text-muted"
            >
              <span role="columnheader" className="w-48 shrink-0">
                Seed
              </span>
              <span role="columnheader" className="w-36 shrink-0">
                Status
              </span>
              <span role="columnheader" className="w-28 shrink-0">
                Trigger
              </span>
              <span role="columnheader" className="w-28 shrink-0">
                Addresses
              </span>
              <span role="columnheader" className="w-28 shrink-0">
                Responded
              </span>
              <span role="columnheader" className="w-24 shrink-0">
                New
              </span>
              <span role="columnheader" className="w-32 shrink-0">
                Started
              </span>
            </div>

            {rows.map((run) => (
              <Link
                key={run.id}
                to="/devices/discovery/runs/$runId"
                params={{ runId: run.id }}
                role="row"
                className="flex h-row items-center gap-4 border-b border-subtle text-table-cell text-secondary transition-colors duration-hover hover:bg-raised focus-visible:ring-2 focus-visible:ring-accent focus-visible:outline-none"
              >
                <span role="cell" className="w-48 shrink-0 truncate text-primary">
                  {run.seedName}
                </span>
                <span role="cell" className="w-36 shrink-0">
                  <Badge tone={runStatusTones[run.status]}>{runStatusLabels[run.status]}</Badge>
                </span>
                <span role="cell" className="w-28 shrink-0">
                  {runTriggerLabels[run.trigger]}
                </span>
                <span role="cell" className="w-28 shrink-0 tabular-nums">
                  {run.addressCount.toLocaleString()}
                </span>
                <span role="cell" className="w-28 shrink-0 tabular-nums">
                  {run.respondedCount.toLocaleString()}
                </span>
                <span role="cell" className="w-24 shrink-0 tabular-nums">
                  {run.newCandidateCount.toLocaleString()}
                </span>
                <span role="cell" className="w-32 shrink-0">
                  <Timestamp value={run.startedAt} />
                </span>
              </Link>
            ))}
          </div>

          {runs.hasNextPage && (
            <div className="mt-gutter flex justify-center">
              <Button
                variant="secondary"
                disabled={runs.isFetchingNextPage}
                onClick={() => void runs.fetchNextPage()}
              >
                Load more
              </Button>
            </div>
          )}
        </>
      )}
    </Card>
  );
}
