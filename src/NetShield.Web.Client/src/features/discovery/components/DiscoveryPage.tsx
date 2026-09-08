import { Link } from '@tanstack/react-router';

import { PageHeader } from '@/components/layout/PageHeader';
import { Button } from '@/components/ui/Button';
import { Tabs, type TabDefinition } from '@/components/ui/Tabs';
import { CandidateReview } from '@/features/discovery/components/CandidateReview';
import { IgnoreList } from '@/features/discovery/components/IgnoreList';
import { RunList } from '@/features/discovery/components/RunList';
import { SeedList } from '@/features/discovery/components/SeedList';
import type { DiscoveryTab } from '@/features/discovery/components/discoveryTabs';

const tabs: readonly TabDefinition[] = [
  { id: 'candidates', label: 'Candidates' },
  { id: 'runs', label: 'Runs' },
  { id: 'seeds', label: 'Seeds' },
  { id: 'ignored', label: 'Ignore list' },
];

interface DiscoveryPageProps {
  readonly tab: DiscoveryTab;
  readonly onTabChange: (tab: DiscoveryTab) => void;
}

/**
 * The discovery screen, under Devices.
 *
 * It is not a sidebar entry. The sidebar comes from `docs/design/reference-dashboard.png`, which
 * has no Discovery row, and DESIGN.md §9 admits no invented visual direction — so discovery
 * lives beneath the area it belongs to and is reached from the device list.
 *
 * Candidates is the default tab because it is the question the screen exists to answer: what has
 * NetShield found that nobody has decided about yet.
 */
export function DiscoveryPage({ tab, onTabChange }: DiscoveryPageProps) {
  return (
    <>
      <div className="mb-gutter flex items-start justify-between gap-4">
        <PageHeader
          title="Discovery"
          subtitle="What NetShield has found on the network, and where it is looking."
        />
        <Link to="/devices">
          <Button variant="secondary">Back to devices</Button>
        </Link>
      </div>

      <div className="mb-gutter">
        <Tabs
          label="Discovery sections"
          tabs={tabs}
          active={tab}
          onChange={(id) => {
            onTabChange(id as DiscoveryTab);
          }}
        />
      </div>

      <div role="tabpanel" id={`panel-${tab}`} aria-labelledby={`tab-${tab}`}>
        {tab === 'candidates' && <CandidateReview />}
        {tab === 'runs' && <RunList />}
        {tab === 'seeds' && <SeedList />}
        {tab === 'ignored' && <IgnoreList />}
      </div>
    </>
  );
}
