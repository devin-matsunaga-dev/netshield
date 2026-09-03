interface PageHeaderProps {
  readonly title: string;
  readonly subtitle: string;
}

/** The page header row (DESIGN.md §5): title and subtitle on the left. */
export function PageHeader({ title, subtitle }: PageHeaderProps) {
  return (
    <div className="mb-gutter">
      <h1 className="text-page-title text-primary">{title}</h1>
      <p className="text-page-subtitle text-secondary">{subtitle}</p>
    </div>
  );
}
