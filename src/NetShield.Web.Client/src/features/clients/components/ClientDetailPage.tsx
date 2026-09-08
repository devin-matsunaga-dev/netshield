import { useQuery } from '@tanstack/react-query';
import { Link, useNavigate } from '@tanstack/react-router';

import { PageHeader } from '@/components/layout/PageHeader';
import { Button } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';
import { ErrorState } from '@/components/ui/ErrorState';
import { Skeleton } from '@/components/ui/Skeleton';
import { Tabs, type TabDefinition } from '@/components/ui/Tabs';
import { clientQuery } from '@/features/clients/api/clientQueries';
import { ClientIpHistoryTab } from '@/features/clients/components/ClientIpHistoryTab';
import { ClientOverviewTab } from '@/features/clients/components/ClientOverviewTab';
import { ClientPortHistoryTab } from '@/features/clients/components/ClientPortHistoryTab';
import type { ClientTab } from '@/features/clients/components/clientTabs';

interface ClientDetailPageProps {
  readonly clientId: string;
  readonly tab: ClientTab;
}

/**
 * One client, in tabs: what it is now, which addresses it has held, which ports have reported it.
 *
 * The active tab is a URL search parameter rather than component state, so a tab is a place that
 * can be linked to, refreshed and arrived at from the row menu — the same reason the list's
 * filters live in the address.
 *
 * No tab is gated. Everything here is behind `InventoryRead`, which every role holds, and there
 * is nothing to write: a client is something NetShield observed rather than something an operator
 * maintains.
 */
export function ClientDetailPage({ clientId, tab }: ClientDetailPageProps) {
  const navigate = useNavigate();
  const client = useQuery(clientQuery(clientId));

  const tabs: TabDefinition[] = [
    { id: 'overview', label: 'Overview' },
    { id: 'addresses', label: 'Address history' },
    { id: 'ports', label: 'Port history' },
  ];

  const active = tabs.some((candidate) => candidate.id === tab) ? tab : 'overview';

  if (client.isPending) {
    return (
      <>
        <PageHeader title="Client" subtitle="Loading…" />
        <Card>
          <div role="status" className="space-y-3" aria-busy="true" aria-label="Loading the client">
            <Skeleton className="w-64" />
            <Skeleton className="w-48" />
            <Skeleton className="w-56" />
          </div>
        </Card>
      </>
    );
  }

  if (client.isError) {
    return (
      <>
        <PageHeader title="Client" subtitle="This client could not be loaded." />
        <Card>
          <ErrorState
            title="The client could not be loaded."
            action="It may have been pruned, or the API may not be reachable. Try again, or go back to the client list."
            onRetry={() => void client.refetch()}
          />
        </Card>
      </>
    );
  }

  const detail = client.data;
  const address = detail.ipBindings[0]?.ipAddress;

  return (
    <>
      <div className="mb-gutter flex items-start justify-between gap-4">
        <div>
          <h1 className="font-mono text-page-title text-primary">{detail.macAddress}</h1>
          <p className="font-mono text-page-subtitle text-secondary">
            {address ?? 'No address currently held'}
          </p>
        </div>
        <Link to="/clients">
          <Button variant="secondary">Back to clients</Button>
        </Link>
      </div>

      <div className="mb-gutter">
        <Tabs
          label="Client sections"
          tabs={tabs}
          active={active}
          onChange={(id) =>
            void navigate({
              to: '/clients/$clientId',
              params: { clientId },
              search: { tab: id as ClientTab },
              replace: true,
            })
          }
        />
      </div>

      <div role="tabpanel" id={`panel-${active}`} aria-labelledby={`tab-${active}`}>
        {active === 'overview' && <ClientOverviewTab client={detail} />}
        {active === 'addresses' && <ClientIpHistoryTab clientId={clientId} />}
        {active === 'ports' && <ClientPortHistoryTab clientId={clientId} />}
      </div>
    </>
  );
}
