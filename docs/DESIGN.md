# DESIGN.md — NetShield visual system

> Binding for every frontend work package. `docs/design/reference-dashboard.png` is the source of truth — **when this document and the screenshot disagree, the screenshot wins.** Do not invent a new visual direction, do not "modernize" it, do not swap the palette, do not add gradients or glass effects that are not in the reference.

## 1. Character

A dark operations console. The chrome recedes; data carries all the color. Every hue in the interface means something — green is healthy, amber is degraded, red is failed, blue is informational or interactive. A surface is never tinted for decoration. If a color appears and does not encode state, it is a bug.

## 2. Stack

- **React 19** + **Vite**, TypeScript strict.
- **Tailwind CSS** for all styling. Tokens below become the Tailwind theme in `tailwind.config.ts`. No raw hex in component code.
- **Recharts** for charts. **React Flow** + `dagre` for topology. **Lucide React** for icons.
- Dark is the default and only complete theme. The light toggle in the header is V1 scope but light mode is derived from these tokens, not separately designed.

## 3. Color tokens

**Surfaces** — three levels, low contrast between them, separated by borders rather than shadows.

| Token | Hex | Use |
|---|---|---|
| `bg-base` | `#080B14` | Page background |
| `bg-sidebar` | `#0B0F1A` | Sidebar and header |
| `bg-surface` | `#111726` | Cards, panels, modals |
| `bg-raised` | `#161D2E` | Table row hover, input fields, nested panels |
| `border-subtle` | `#1C2438` | Default hairline — card edges, table rules |
| `border-strong` | `#2A3448` | Input borders, focused card edges |

**Text**

| Token | Hex | Use |
|---|---|---|
| `text-primary` | `#E8ECF5` | Headings, metric values, table primary column |
| `text-secondary` | `#9BA6BF` | Labels, body, table secondary columns |
| `text-muted` | `#5E6A85` | Timestamps, hints, disabled, column headers |

**Semantic** — each has a solid value for text and marks, and a 12%-alpha tint for icon tiles, badge backgrounds, and chart area fills.

| Token | Solid | Tint | Meaning |
|---|---|---|---|
| `accent` | `#3B82F6` | `#3B82F61F` | Interactive, informational, active nav |
| `success` | `#22C55E` | `#22C55E1F` | Healthy, online, pass |
| `warning` | `#F59E0B` | `#F59E0B1F` | Warning, medium severity, drift |
| `danger` | `#EF4444` | `#EF44441F` | Critical, offline, high severity, fail |
| `info` | `#38BDF8` | `#38BDF81F` | Bandwidth, throughput, neutral telemetry |
| `violet` | `#A855F7` | `#A855F71F` | Clients, identity, users |
| `orange` | `#F97316` | `#F973161F` | Security score, risk |

**Severity mapping is fixed and used everywhere without exception:** High → `danger`, Medium → `warning`, Low → `warning` at 70% opacity for the dot only, Informational → `accent`. Device state: Online → `success`, Warning → `warning`, Offline → `danger`, Unknown → `text-muted`.

**Categorical chart palette**, in order, for series with no semantic meaning (top applications, VLAN breakdowns): `#A855F7`, `#3B82F6`, `#22C55E`, `#38BDF8`, `#64748B`.

## 4. Typography

One family: **Inter**, self-hosted as variable woff2 (no Google Fonts call at runtime — see SPEC §5, no outbound dependency). `ui-monospace, "JetBrains Mono", monospace` for IP addresses, MAC addresses, serials, config diffs, and log lines only.

| Role | Size / line | Weight | Notes |
|---|---|---|---|
| Page title | 24px / 32px | 600 | "Network Overview" |
| Page subtitle | 14px / 20px | 400 | `text-secondary` |
| Card title | 15px / 20px | 600 | "Bandwidth Utilization" |
| Metric value | 30px / 36px | 600 | "1,024", "68%" |
| Metric label | 13px / 18px | 500 | `text-secondary`, above the value |
| Metric caption | 12px / 16px | 400 | `text-muted`, below the value |
| Body | 14px / 20px | 400 | |
| Table header | 12px / 16px | 500 | `text-muted`, **sentence case, not uppercase** |
| Table cell | 13px / 18px | 400 | |
| Badge | 11px / 16px | 500 | |
| Nav item | 14px / 20px | 500 | |

Sentence case everywhere. No tracked-out uppercase labels. Tabular numerals (`font-variant-numeric: tabular-nums`) on every metric value, table number column, and chart axis.

## 5. Layout

- **Sidebar** 200px expanded, 64px collapsed, full height, `bg-sidebar`, 1px right border `border-subtle`. Brand block at top (shield mark + "NetShield" 15px/600 + "Network & Security" 11px `text-muted`). Nav list below. "Collapse" control pinned at the bottom above a top border.
- **Header** 64px, `bg-sidebar`, 1px bottom border. Search field left (max 400px, `bg-raised`, rounded 8px, magnifier icon, ⌘K chip right-aligned inside), then right-aligned: notification bell with count badge, help, theme toggle, and the user block (avatar, name 13px/500, role 11px `text-muted`, chevron).
- **Content** `bg-base`, 24px padding, max width 1600px. Page header row: title + subtitle left, time-range selector and primary action right.
- **Grid** 12 columns, 20px gutter, 20px row gap.
  - KPI strip: 5 equal cards.
  - Main row: topology `col-span-5`, bandwidth `col-span-4`, security status `col-span-3`.
  - Lower row: recent alerts `col-span-5`, top applications `col-span-4`, device health `col-span-3`.
- **Responsive:** ≥1536px as above; 1280–1535px collapses the lower row to 6/6/12; 1024–1279px two columns; <1024px single column with the sidebar as an overlay drawer.

## 6. Components

**Card** — `bg-surface`, 1px `border-subtle`, radius 12px, no shadow. Header row 48px: title left, optional control right (a "View All" ghost button, a unit dropdown, a legend). Body padding 20px. Cards do not nest.

**KPI card** — 20px padding. Top row: 40×40 icon tile (radius 10px, semantic tint background, solid semantic icon at 20px) on the left, label and value stacked to its right. Optional delta line under the value: caret glyph + percentage in `success` or `danger` + " from yesterday" in `text-muted`. A 48px-tall sparkline bleeds to the card's left and right padding edges at the bottom — 2px stroke in the card's semantic color, area fill in its tint, no axes, no grid, no dots.

**Badge** — pill, 11px/500, 2px vertical and 8px horizontal padding, semantic tint background, solid semantic text. Used for device health and compliance status.

**Severity indicator** — a 6px filled dot in the semantic color followed by the label in the same color at 13px/500. Used in tables. Never a full-width colored row.

**Table** — header row 12px `text-muted` with a bottom `border-subtle`; rows 44px with a bottom `border-subtle`, last row borderless; hover `bg-raised`; a trailing 40px column holding a vertical-dots row menu. Zebra striping is not used.

**Donut chart** — 60% inner radius, 2px gap between arcs, centered value at 30px/600 with an 11px `text-muted` label beneath. Legend as a right-hand vertical list: color dot, label, then right-aligned count and percentage. Never a legend under the chart.

**Area chart** — 2px stroke, gradient fill from 20% to 0% alpha of the stroke color, 4px dots at data points, horizontal grid lines only at `border-subtle`, axis labels 11px `text-muted`, tooltip on `bg-raised` with 1px `border-strong` and radius 8px.

**Topology canvas** — `bg-surface` with a subtle 20px dot grid at `#1C2438`. Nodes are 48px icon tiles with a 12px/500 label beneath and a 10px/400 `text-muted` sub-label. Node border encodes state via the semantic colors. Edges are 1.5px `border-strong`, turning `success` when the link is up and carrying traffic. Zoom in / zoom out / fit controls stack vertically at the top-left as 32px `bg-raised` buttons. A state legend sits in the card header, not on the canvas.

**Buttons** — Primary: `accent` fill, white text, radius 8px, 36px tall, 14px/500, 16px horizontal padding. Secondary: `bg-raised` fill, `border-subtle`, `text-primary`. Ghost: transparent, `text-secondary`, `bg-raised` on hover. Destructive uses `danger` fill and always requires a typed confirmation for anything irreversible.

**Nav item** — 40px tall, 8px radius, 20px icon + label, `text-secondary` at rest. Hover: `bg-raised`, `text-primary`. Active: `accent` tint background, `text-primary`, a 3px `accent` bar flush to the sidebar's left edge. A section with children shows a right chevron that rotates 90° when open; children are indented 32px at 13px.

## 7. Motion

Restrained. 150ms `ease-out` on hover and focus. 200ms on panel and drawer open. Chart series animate once on mount at 400ms and never again on refetch — a live dashboard that re-animates every 30 seconds is unusable. Live value updates flash the number's background in its semantic tint for 400ms and fade. No entrance animations on page load, no scroll-triggered reveals, no skeleton shimmer beyond a 1.5s opacity pulse. All of it inside `@media (prefers-reduced-motion: no-preference)`.

## 8. Writing

Sentence case for every label, button, heading, and column. Buttons name the action and keep the same word through the flow: "Add device" → toast "Device added". Empty states state the situation and the next step: "No devices yet. Run discovery or add one manually." Errors state what failed and what to do: "Could not reach 192.168.1.1 over SNMP. Check the credential profile and try again." Never apologize, never say "Oops", never use an exclamation mark. Timestamps are relative under 24 hours ("2 min ago") and absolute beyond, always with the user's configured timezone shown on hover.

## 9. Non-negotiables

1. Never introduce a color outside the token table.
2. Never use color as the only signal — pair it with a label, an icon, or a shape.
3. Never place a raw hex value in a component.
4. Never add a shadow. Separation comes from borders.
5. Never uppercase a label.
6. Never render an unbounded list — virtualize past 100 rows.
7. Every chart has an accessible text summary and a table fallback.
