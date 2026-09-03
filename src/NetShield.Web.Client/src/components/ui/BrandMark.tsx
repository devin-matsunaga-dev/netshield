interface BrandMarkProps {
  /** Tailwind size utility for the rendered mark — `size-6` in the sidebar, `size-8` signed out. */
  readonly className: string;
}

/**
 * The NetShield shield mark (DESIGN.md §5).
 *
 * Decorative in every place it is used: the product name sits beside it and is what a screen
 * reader announces, so the mark is hidden rather than described twice.
 *
 * Served from `public/`, not imported, so the same file backs the brand block and the browser
 * tab and there is only ever one of it to replace.
 */
export function BrandMark({ className }: BrandMarkProps) {
  return <img src="/brand/netshield-mark.png" alt="" aria-hidden className={className} />;
}
