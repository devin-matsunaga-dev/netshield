import { createFileRoute, useNavigate } from '@tanstack/react-router';

import { DiscoveryPage } from '@/features/discovery/components/DiscoveryPage';
import { discoveryTabs, type DiscoveryTab } from '@/features/discovery/components/discoveryTabs';

/**
 * The discovery screen. Under `/devices` rather than in the sidebar: the sidebar is the
 * reference screenshot's and has no Discovery row (DESIGN.md §9).
 */
export const Route = createFileRoute('/_app/devices/discovery/')({
  validateSearch: (search: Record<string, unknown>): { tab?: DiscoveryTab } =>
    typeof search['tab'] === 'string' &&
    (discoveryTabs as readonly string[]).includes(search['tab'])
      ? { tab: search['tab'] as DiscoveryTab }
      : {},
  component: function Discovery() {
    const navigate = useNavigate();

    return (
      <DiscoveryPage
        tab={Route.useSearch().tab ?? 'candidates'}
        onTabChange={(next) => {
          void navigate({ to: '/devices/discovery', search: { tab: next }, replace: true });
        }}
      />
    );
  },
});
