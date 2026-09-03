import { Link } from '@tanstack/react-router';

import { PageHeader } from '@/components/layout/PageHeader';
import { Card } from '@/components/ui/Card';

/** What a URL nothing serves renders. It says what happened and what to do (DESIGN.md §8). */
export function NotFoundPage() {
  return (
    <>
      <PageHeader title="Page not found" subtitle="That address does not match any screen." />
      <Card>
        <p className="text-body text-secondary">
          Check the address, or{' '}
          <Link to="/overview" className="text-accent">
            go to the overview
          </Link>
          .
        </p>
      </Card>
    </>
  );
}
