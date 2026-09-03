import { BrandMark } from '@/components/ui/BrandMark';

interface BrandProps {
  readonly collapsed: boolean;
}

/**
 * The sidebar's brand block: shield mark, product name, and what the product is
 * (DESIGN.md §5).
 */
export function Brand({ collapsed }: BrandProps) {
  return (
    <div className="flex h-header shrink-0 items-center gap-3 border-b border-subtle px-4">
      <BrandMark className="size-6 shrink-0" />
      {!collapsed && (
        <span className="min-w-0">
          <span className="block truncate text-brand text-primary">NetShield</span>
          <span className="block truncate text-brand-caption text-muted">
            Network &amp; Security
          </span>
        </span>
      )}
    </div>
  );
}
