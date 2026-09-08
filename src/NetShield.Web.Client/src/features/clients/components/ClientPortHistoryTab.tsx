import { useInfiniteQuery } from '@tanstack/react-query';

import { Button } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';
import { EmptyState } from '@/components/ui/EmptyState';
import { ErrorState } from '@/components/ui/ErrorState';
import { Skeleton } from '@/components/ui/Skeleton';
import { Timestamp } from '@/components/ui/Timestamp';
import { clientPortHistoryQuery } from '@/features/clients/api/clientQueries';
import {
  formatDuration,
  observationSourceLabels,
} from '@/features/clients/components/clientLabels';
import { toNumber } from '@/lib/apiNumber';

/**
 * Every port that has reported this client, as closed intervals, newest first.
 *
 * "Where was this plugged in on Tuesday" is the question, and it is the one thing a port interval
 * makes answerable at all. A row per device rather than per client: a MAC is learned by every
 * bridge on the path to it, so one client legitimately has an open interval on its access switch
 * and one on every switch above it at the same time.
 */
export function ClientPortHistoryTab({ clientId }: { readonly clientId: string }) {
  const history = useInfiniteQuery(clientPortHistoryQuery(clientId));

  const rows = history.data?.pages.flatMap((page) => page.items) ?? [];

  if (history.isPending) {
    return (
      <Card>
        <div
          role="status"
          className="space-y-3"
          aria-busy="true"
          aria-label="Loading the port history"
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
          title="The port history could not be loaded."
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
          title="No switch has ever reported this client."
          action="Ports are learned from a switch's MAC address table. Read one from its own page, or wait for the schedule."
        />
      </Card>
    );
  }

  return (
    <Card>
      <table className="w-full text-table-cell">
        <caption className="sr-only">
          Every port that has reported this client, newest first
        </caption>
        <thead>
          <tr className="border-b border-subtle text-left text-table-header text-muted">
            <th scope="col" className="py-2 font-medium">
              Device
            </th>
            <th scope="col" className="py-2 font-medium">
              Port
            </th>
            <th scope="col" className="py-2 font-medium">
              VLAN
            </th>
            <th scope="col" className="py-2 font-medium">
              Addresses on port
            </th>
            <th scope="col" className="py-2 font-medium">
              Reported from
            </th>
            <th scope="col" className="py-2 font-medium">
              Until
            </th>
            <th scope="col" className="py-2 font-medium">
              For
            </th>
          </tr>
        </thead>
        <tbody>
          {rows.map((binding) => {
            const ifIndex = toNumber(binding.ifIndex);
            const vlanId = toNumber(binding.vlanId);
            const count = toNumber(binding.macCountOnPort);

            return (
              <tr key={binding.id} className="border-b border-subtle text-secondary last:border-0">
                <td className="py-2 text-primary">
                  {binding.deviceHostname ?? (
                    <span className="text-muted">A device that has been removed</span>
                  )}
                </td>
                <td className="py-2 font-mono">
                  {binding.interfaceName ?? `ifIndex ${ifIndex?.toString() ?? '—'}`}
                </td>
                <td className="py-2 tabular-nums">{vlanId === null ? '—' : vlanId.toString()}</td>
                <td className="py-2 tabular-nums" title={observationSourceLabels[binding.source]}>
                  {count === null ? '—' : count.toString()}
                </td>
                <td className="py-2 tabular-nums">
                  <Timestamp value={binding.observedFrom} />
                </td>
                <td className="py-2 tabular-nums">
                  {binding.observedTo === null ? (
                    <span className="text-success">Still reported</span>
                  ) : (
                    <Timestamp value={binding.observedTo} />
                  )}
                </td>
                <td className="py-2 tabular-nums">
                  {formatDuration(binding.observedFrom, binding.observedTo)}
                </td>
              </tr>
            );
          })}
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
