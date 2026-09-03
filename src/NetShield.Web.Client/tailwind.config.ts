import type { Config } from 'tailwindcss';

/**
 * The NetShield design tokens, as the Tailwind theme (DESIGN.md §2).
 *
 * Every name here comes from DESIGN.md §3 and §4 and nothing else does — DESIGN.md §9 admits no
 * colour outside the token table and no raw hex in a component, and `theme.test.ts` fails the
 * build if either happens. The values are CSS custom properties rather than literals so that the
 * header's light/dark toggle can swap a palette without every component knowing; the properties
 * themselves are assigned once, in `src/styles/theme.css`.
 */
const config: Config = {
  content: ['./index.html', './src/**/*.{ts,tsx}'],
  theme: {
    extend: {
      colors: {
        // Semantic. Each has a solid value for text and marks and a 12%-alpha tint for icon
        // tiles, badge backgrounds and chart area fills (DESIGN.md §3).
        accent: 'var(--ns-accent)',
        'accent-tint': 'var(--ns-accent-tint)',
        success: 'var(--ns-success)',
        'success-tint': 'var(--ns-success-tint)',
        warning: 'var(--ns-warning)',
        'warning-tint': 'var(--ns-warning-tint)',
        danger: 'var(--ns-danger)',
        'danger-tint': 'var(--ns-danger-tint)',
        info: 'var(--ns-info)',
        'info-tint': 'var(--ns-info-tint)',
        violet: 'var(--ns-violet)',
        'violet-tint': 'var(--ns-violet-tint)',
        orange: 'var(--ns-orange)',
        'orange-tint': 'var(--ns-orange-tint)',

        // The categorical chart palette, in order, for series with no semantic meaning.
        'chart-1': 'var(--ns-chart-1)',
        'chart-2': 'var(--ns-chart-2)',
        'chart-3': 'var(--ns-chart-3)',
        'chart-4': 'var(--ns-chart-4)',
        'chart-5': 'var(--ns-chart-5)',
      },

      // Surfaces: three levels, low contrast between them, separated by borders not shadows.
      backgroundColor: {
        base: 'var(--ns-bg-base)',
        sidebar: 'var(--ns-bg-sidebar)',
        surface: 'var(--ns-bg-surface)',
        raised: 'var(--ns-bg-raised)',
      },
      borderColor: {
        subtle: 'var(--ns-border-subtle)',
        strong: 'var(--ns-border-strong)',
      },
      textColor: {
        primary: 'var(--ns-text-primary)',
        secondary: 'var(--ns-text-secondary)',
        muted: 'var(--ns-text-muted)',
      },
      ringColor: {
        accent: 'var(--ns-accent)',
      },

      fontFamily: {
        // Self-hosted variable woff2. No font CDN at runtime (SPEC.md §5).
        sans: ['Inter Variable', 'Inter', 'system-ui', 'sans-serif'],
        // IP and MAC addresses, serials, config diffs and log lines only (DESIGN.md §4).
        mono: ['ui-monospace', 'JetBrains Mono', 'monospace'],
      },

      // DESIGN.md §4, verbatim. Size, line height and weight travel together so a heading
      // cannot be given the right size at the wrong weight.
      fontSize: {
        'page-title': ['24px', { lineHeight: '32px', fontWeight: '600' }],
        'page-subtitle': ['14px', { lineHeight: '20px', fontWeight: '400' }],
        'card-title': ['15px', { lineHeight: '20px', fontWeight: '600' }],
        'metric-value': ['30px', { lineHeight: '36px', fontWeight: '600' }],
        'metric-label': ['13px', { lineHeight: '18px', fontWeight: '500' }],
        'metric-caption': ['12px', { lineHeight: '16px', fontWeight: '400' }],
        body: ['14px', { lineHeight: '20px', fontWeight: '400' }],
        'table-header': ['12px', { lineHeight: '16px', fontWeight: '500' }],
        'table-cell': ['13px', { lineHeight: '18px', fontWeight: '400' }],
        badge: ['11px', { lineHeight: '16px', fontWeight: '500' }],
        'nav-item': ['14px', { lineHeight: '20px', fontWeight: '500' }],
        // The brand block and a nav section's children (DESIGN.md §5, §6).
        brand: ['15px', { lineHeight: '20px', fontWeight: '600' }],
        'brand-caption': ['11px', { lineHeight: '16px', fontWeight: '400' }],
        'nav-child': ['13px', { lineHeight: '18px', fontWeight: '500' }],
      },

      // DESIGN.md §5 and §6 geometry.
      spacing: {
        sidebar: '200px',
        'sidebar-collapsed': '64px',
        header: '64px',
        'card-header': '48px',
        'nav-item': '40px',
        'nav-child-indent': '32px',
        control: '36px',
        'icon-tile': '40px',
        'row-menu': '40px',
        row: '44px',
        content: '24px',
        gutter: '20px',
      },
      maxWidth: {
        content: '1600px',
        search: '400px',
      },
      borderRadius: {
        card: '12px',
        tile: '10px',
        control: '8px',
      },
      transitionDuration: {
        // DESIGN.md §7: 150ms on hover and focus, 200ms on a panel or drawer.
        hover: '150ms',
        panel: '200ms',
      },
    },
  },
  plugins: [],
};

export default config;
