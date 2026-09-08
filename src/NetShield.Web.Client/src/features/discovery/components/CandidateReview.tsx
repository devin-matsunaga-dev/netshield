import { useInfiniteQuery } from '@tanstack/react-query';
import { useState } from 'react';

import type { Schemas } from '@/api/types';
import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';
import { EmptyState } from '@/components/ui/EmptyState';
import { ErrorState } from '@/components/ui/ErrorState';
import { Select } from '@/components/ui/Select';
import { Skeleton } from '@/components/ui/Skeleton';
import { Timestamp } from '@/components/ui/Timestamp';
import { useToast } from '@/lib/toast';
import { formatRoundTrip } from '@/lib/apiNumber';
import type { DeviceRequestError } from '@/features/devices/api/deviceMutations';
import {
  useIgnoreCandidate,
  usePromoteCandidate,
} from '@/features/discovery/api/discoveryMutations';
import {
  discoveryCandidatesQuery,
  type DiscoveryCandidateSummary,
} from '@/features/discovery/api/discoveryQueries';
import {
  candidateStatusLabels,
  candidateStatusTones,
} from '@/features/discovery/components/discoveryLabels';
import { PromoteCandidateDialog } from '@/features/discovery/components/PromoteCandidateDialog';
import { RequirePermission } from '@/features/session/components/RequirePermission';

/**
 * The review step SPEC.md §2 asks for: "results appear as reviewable candidates rather than
 * auto-created devices".
 *
 * WP-1.6 built the candidates, the promotion and the dismissal behind three endpoints and no
 * screen, so half of that sentence was true and half of it was not. This is the other half.
 *
 * A candidate is a bare address and how many sweeps have seen it. There is no hostname, no
 * vendor and no reverse lookup: identifying a responder during a sweep would mean carrying
 * several credentials on one lease, which is a change to the collector contract.
 */
export function CandidateReview() {
  const [status, setStatus] = useState<Schemas['DiscoveryCandidateStatus'] | undefined>('New');
  const [promoting, setPromoting] = useState<DiscoveryCandidateSummary | null>(null);

  const candidates = useInfiniteQuery(discoveryCandidatesQuery(status));
  const promote = usePromoteCandidate();
  const ignore = useIgnoreCandidate();
  const toast = useToast();

  const rows = candidates.data?.pages.flatMap((page) => page.items) ?? [];

  return (
    <Card title="Candidates">
      <div className="mb-gutter flex items-end justify-between gap-4">
        <div className="w-56">
          <Select
            label="Status"
            anyLabel="All candidates"
            value={status ?? ''}
            onChange={(event) => {
              setStatus(
                event.target.value === ''
                  ? undefined
                  : (event.target.value as Schemas['DiscoveryCandidateStatus']),
              );
            }}
            options={[
              { value: 'New', label: candidateStatusLabels.New },
              { value: 'Promoted', label: candidateStatusLabels.Promoted },
              { value: 'Ignored', label: candidateStatusLabels.Ignored },
            ]}
          />
        </div>
        {candidates.hasNextPage && (
          <Button
            variant="secondary"
            disabled={candidates.isFetchingNextPage}
            onClick={() => void candidates.fetchNextPage()}
          >
            Load more
          </Button>
        )}
      </div>

      {candidates.isPending ? (
        <div role="status" className="space-y-3" aria-busy="true" aria-label="Loading candidates">
          {Array.from({ length: 5 }, (_, index) => (
            <Skeleton key={index} className="h-8 w-full" />
          ))}
        </div>
      ) : candidates.isError ? (
        <ErrorState
          title="The candidates could not be loaded."
          action="NetShield could not reach the API. Check that it is running and try again."
          onRetry={() => void candidates.refetch()}
        />
      ) : rows.length === 0 ? (
        <EmptyState
          title={
            status === 'New' ? 'Nothing is waiting for review.' : 'No candidates with this status.'
          }
          action="Discovery adds a candidate for every address that answers a sweep and is not already a device."
        />
      ) : (
        <div role="table" aria-label="Discovery candidates" className="flex flex-col">
          <div
            role="row"
            className="flex items-center gap-4 border-b border-subtle py-2 text-table-header text-muted"
          >
            <span role="columnheader" className="w-44 shrink-0">
              Address
            </span>
            <span role="columnheader" className="w-36 shrink-0">
              Status
            </span>
            <span role="columnheader" className="w-24 shrink-0">
              Sweeps
            </span>
            <span role="columnheader" className="w-28 shrink-0">
              Round trip
            </span>
            <span role="columnheader" className="w-32 shrink-0">
              First seen
            </span>
            <span role="columnheader" className="w-32 shrink-0">
              Last seen
            </span>
            <span role="columnheader" className="flex-1">
              <span className="sr-only">Actions</span>
            </span>
          </div>

          {rows.map((candidate) => (
            <div
              key={candidate.id}
              role="row"
              className="flex h-row items-center gap-4 border-b border-subtle text-table-cell text-secondary hover:bg-raised"
            >
              <span role="cell" className="w-44 shrink-0 font-mono text-primary tabular-nums">
                {candidate.address}
              </span>
              <span role="cell" className="w-36 shrink-0">
                <Badge tone={candidateStatusTones[candidate.status]}>
                  {candidateStatusLabels[candidate.status]}
                </Badge>
              </span>
              <span role="cell" className="w-24 shrink-0 tabular-nums">
                {candidate.timesSeen}
              </span>
              <span role="cell" className="w-28 shrink-0 tabular-nums">
                {formatRoundTrip(candidate.lastRttMilliseconds)}
              </span>
              <span role="cell" className="w-32 shrink-0">
                <Timestamp value={candidate.firstSeenAt} />
              </span>
              <span role="cell" className="w-32 shrink-0">
                <Timestamp value={candidate.lastSeenAt} />
              </span>
              <span role="cell" className="flex flex-1 justify-end gap-2">
                {candidate.status === 'New' && (
                  <RequirePermission permission="InventoryWrite">
                    <Button
                      variant="secondary"
                      onClick={() => {
                        setPromoting(candidate);
                      }}
                    >
                      Promote
                    </Button>
                    <Button
                      variant="ghost"
                      disabled={ignore.isPending}
                      onClick={() => {
                        ignore.mutate(candidate.id, {
                          onSuccess: () => {
                            toast.announce(`${candidate.address} added to the ignore list`);
                          },
                          onError: (error) => {
                            toast.announce(error.message, 'danger');
                          },
                        });
                      }}
                    >
                      Ignore
                    </Button>
                  </RequirePermission>
                )}
              </span>
            </div>
          ))}
        </div>
      )}

      {promoting !== null && (
        <PromoteCandidateDialog
          candidate={promoting}
          pending={promote.isPending}
          error={(promote.error as DeviceRequestError | null) ?? null}
          onCancel={() => {
            setPromoting(null);
          }}
          onPromote={(request) => {
            promote.mutate(
              { id: promoting.id, request },
              {
                onSuccess: () => {
                  setPromoting(null);
                  toast.announce('Device added');
                },
                onError: (error) => {
                  toast.announce(error.message, 'danger');
                },
              },
            );
          }}
        />
      )}
    </Card>
  );
}
