import { useInfiniteQuery } from '@tanstack/react-query';

import { Button } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';
import { EmptyState } from '@/components/ui/EmptyState';
import { ErrorState } from '@/components/ui/ErrorState';
import { Skeleton } from '@/components/ui/Skeleton';
import { Timestamp } from '@/components/ui/Timestamp';
import { clientIpHistoryQuery } from '@/features/clients/api/clientQueries';
import {
  formatDuration,
  observationSourceLabels,
} from '@/features/clients/components/clientLabels';

/**
 * Every address this client has held, as closed intervals, newest first.
 *
 * The table an operator reads to answer "what was this thing on at 14:03", and the human-facing
 * form of exactly what `ResolveAssetAt` resolves through. An open interval says "still held"
 * rather than showing a blank, because "this is current" and "nobody filled this in" are
 * different facts.
 *
 * A plain table rather than a virtualized grid: this is one client's history behind a tab and a
 * page is a hundred rows, well under the hundred-row threshold DESIGN.md §9.6 sets.
 */
export function ClientIpHistoryTab({ clientId }: { readonly clientId: string }) {
  const history = useInfiniteQuery(clientIpHistoryQuery(clientId));

  const rows = history.data?.pages.flatMap((page) => page.items) ?? [];

  if (history.isPending) {
    return (
      <Card>
        <div
          role="status"
          className="space-y-3"
          aria-busy="true"
          aria-label="Loading the address history"
        >
          {Array.from({ length: 6 }, (_, index) => (
            <Skeleton key={index} className="h-8 w-full" />
          ))}
        </div>
      </Card>
    );
  }

  if (history.isError) {
    return (
      <Card>
        <ErrorState
          title="The address history could not be loaded."
          action="NetShield could not reach the API. Check that it is running and try again."
          onRetry={() => void history.refetch()}
        />
      </Card>
    );
  }

  if (rows.length === 0) {
    return (
      <Card>
        <EmptyState
          title="This client has never been seen holding an address."
          action="Addresses are learned from a router or layer-3 switch's ARP table. Read one from its own page, or wait for the schedule."
        />
      </Card>
    );
  }

  return (
    <Card>
      <table className="w-full text-table-cell">
        <caption className="sr-only">Every address this client has held, newest first</caption>
        <thead>
          <tr className="border-b border-subtle text-left text-table-header text-muted">
            <th scope="col" className="py-2 font-medium">
              Address
            </th>
            <th scope="col" className="py-2 font-medium">
              Held from
            </th>
            <th scope="col" className="py-2 font-medium">
              Held until
            </th>
            <th scope="col" className="py-2 font-medium">
              For
            </th>
            <th scope="col" className="py-2 font-medium">
              Evidence
            </th>
          </tr>
        </thead>
        <tbody>
          {rows.map((binding) => (
            <tr key={binding.id} className="border-b border-subtle text-secondary last:border-0">
              <td className="py-2 font-mono text-primary tabular-nums">{binding.ipAddress}</td>
              <td className="py-2 tabular-nums">
                <Timestamp value={binding.observedFrom} />
              </td>
              <td className="py-2 tabular-nums">
                {binding.observedTo === null ? (
                  <span className="text-success">Still held</span>
                ) : (
                  <Timestamp value={binding.observedTo} />
                )}
              </td>
              <td className="py-2 tabular-nums">
                {formatDuration(binding.observedFrom, binding.observedTo)}
              </td>
              <td className="py-2">
                {observationSourceLabels[binding.source]}
                {binding.deviceHostname === null ? '' : ` · ${binding.deviceHostname}`}
              </td>
            </tr>
          ))}
        </tbody>
      </table>

      {history.hasNextPage && (
        <div className="mt-gutter">
          <Button
            variant="secondary"
            disabled={history.isFetchingNextPage}
            onClick={() => void history.fetchNextPage()}
          >
            {history.isFetchingNextPage ? 'Loading…' : 'Load older'}
          </Button>
        </div>
      )}
    </Card>
  );
}
