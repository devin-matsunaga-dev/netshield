# STATUS.md — NetShield

> Single source of truth for where the build is. Updated by the agent at the end of every package. If this file is wrong, the next session starts wrong.

## Current WP

**WP-0.1 — Solution skeleton**
Branch: `feat/wp-0.1-skeleton`
Phase: 0 — Foundation
Sensitive: no

## Phase progress

| Phase | Packages | Done | State |
|---|---|---|---|
| 0 — Foundation | 7 | 0 | In progress |
| 1 — Inventory & discovery | 8 | 0 | Not started |
| 2 — Topology | 4 | 0 | Not started |
| 3 — Telemetry | 6 | 0 | Not started |
| 4 — Flows | 5 | 0 | Not started |
| 5 — Logs | 5 | 0 | Not started |
| 6 — Alerting | 7 | 0 | Not started |
| 7 — Config, compliance, vulns | 7 | 0 | Not started |
| 8 — Dashboard, reporting, hardening | 7 | 0 | Not started |

## Completed packages

_None yet._

## In flight / noticed

Things spotted during a package that are out of its scope. Do not fix them in the current package — record them here and address them in a package of their own.

- Lab environment is not yet stood up. Containerlab plus an `snmpsim` fixture corpus is needed before WP-1.5. See `WORKFLOW.md` § Test data.

## Blocked

_Nothing blocked._

## Open questions for the human

- Which vendors are actually present in the target estate? `SPEC.md` §4 lists seven; trimming that list before Phase 1 removes real work from WP-1.5, WP-5.2, WP-7.1, and WP-7.4.
- Is there an existing vulnerability scanner whose output format should drive WP-7.6, or should all three importers be built?
