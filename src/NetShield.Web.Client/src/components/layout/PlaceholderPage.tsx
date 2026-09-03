import { PageHeader } from '@/components/layout/PageHeader';
import { Card } from '@/components/ui/Card';

interface PlaceholderPageProps {
  readonly title: string;
  readonly subtitle: string;
  /** What builds this screen, so the empty state says what happens next (DESIGN.md §8). */
  readonly arrivesIn: string;
}

/**
 * What every route renders until the package that owns it builds the screen. An empty state
 * states the situation and the next step; it never apologises (DESIGN.md §8).
 */
export function PlaceholderPage({ title, subtitle, arrivesIn }: PlaceholderPageProps) {
  return (
    <>
      <PageHeader title={title} subtitle={subtitle} />
      <Card>
        <p className="text-body text-secondary">
          Nothing is built here yet. This screen arrives with {arrivesIn}.
        </p>
      </Card>
    </>
  );
}
