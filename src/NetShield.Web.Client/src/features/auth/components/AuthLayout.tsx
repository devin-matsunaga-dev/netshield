import { ShieldCheck } from 'lucide-react';
import type { ReactNode } from 'react';

interface AuthLayoutProps {
  readonly title: string;
  readonly subtitle: string;
  readonly children: ReactNode;
}

/**
 * The frame the signed-out screens render in.
 *
 * `docs/design/reference-dashboard.png` has no sign-in screen, and DESIGN.md forbids inventing a
 * visual direction — so this invents none. It is the §6 card on the §3 page background, the §5
 * brand block, and nothing that is not already in the token table: no gradient, no glass, no
 * shadow, separation by border alone (DESIGN.md §9.4).
 */
export function AuthLayout({ title, subtitle, children }: AuthLayoutProps) {
  return (
    <main className="flex min-h-dvh items-center justify-center bg-base p-content">
      <div className="w-full max-w-sm">
        <div className="mb-gutter flex items-center gap-3">
          <ShieldCheck aria-hidden className="size-8 shrink-0 text-accent" />
          <span>
            <span className="block text-brand text-primary">NetShield</span>
            <span className="block text-brand-caption text-muted">Network &amp; Security</span>
          </span>
        </div>

        <section className="rounded-card border border-subtle bg-surface p-gutter">
          <h1 className="text-page-title text-primary">{title}</h1>
          <p className="mt-1 mb-gutter text-page-subtitle text-secondary">{subtitle}</p>
          {children}
        </section>
      </div>
    </main>
  );
}
