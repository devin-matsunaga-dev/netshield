# STATUS.md — NetShield

> Single source of truth for where the build is. Updated by the agent at the end of every package. If this file is wrong, the next session starts wrong.

## Current WP

**WP-0.2 — Aspire orchestration**
Branch: `feat/wp-0.2-aspire`
Phase: 0 — Foundation
Sensitive: no

## Phase progress

| Phase | Packages | Done | State |
|---|---|---|---|
| 0 — Foundation | 7 | 1 | In progress |
| 1 — Inventory & discovery | 8 | 0 | Not started |
| 2 — Topology | 4 | 0 | Not started |
| 3 — Telemetry | 6 | 0 | Not started |
| 4 — Flows | 5 | 0 | Not started |
| 5 — Logs | 5 | 0 | Not started |
| 6 — Alerting | 7 | 0 | Not started |
| 7 — Config, compliance, vulns | 7 | 0 | Not started |
| 8 — Dashboard, reporting, hardening | 7 | 0 | Not started |

## Completed packages

- **WP-0.1 — Solution skeleton** (2026-09-03, `feat/wp-0.1-skeleton`). `NetShield.sln` with 19 projects on `net10.0`: AppHost, ServiceDefaults, Contracts, Platform, Web.Host, Ingest, ten module projects under `src/Modules/`, three .NET test projects. `Directory.Build.props` owns the target framework, nullable, implicit usings, and warnings-as-errors; `Directory.Packages.props` owns every package version. Project references follow `ARCHITECTURE.md` §4 — no module references another module. Five tests in `NetShield.ArchitectureTests` enforce those MSBuild invariants against the project files on disk.

## In flight / noticed

Things spotted during a package that are out of its scope. Do not fix them in the current package — record them here and address them in a package of their own.

- Lab environment is not yet stood up. Containerlab plus an `snmpsim` fixture corpus is needed before WP-1.5. See `WORKFLOW.md` § Test data.
- `docker-compose.yml` is in the `CONVENTIONS.md` §1 layout and `ARCHITECTURE.md` §2 names Docker Compose as the deployment orchestrator, but no Phase 0 package creates it. It needs a package of its own, most naturally in Phase 8 alongside hardening.
- `NetShield.UnitTests` and `NetShield.IntegrationTests` exist but hold no tests — there was nothing to test in WP-0.1. WP-0.3 gives them their first ones and adds the Testcontainers dependency `CONVENTIONS.md` §7 requires for integration tests.
- `NetShield.ArchitectureTests` currently has no project references; its WP-0.1 tests read the `.csproj` files directly. WP-0.3 adds the module-reference rules from `ARCHITECTURE.md` §4, which will need assembly-level references and a rule library.
- `.editorconfig` carries placeholder sections for `*.py` and `*.{ts,tsx,...}` ahead of the collector (WP-1.3) and the SPA (WP-0.6). Revisit them when those toolchains land — `ruff` and Prettier own formatting there, not `dotnet format`.

## Blocked

_Nothing blocked._

## Open questions for the human

- Which vendors are actually present in the target estate? `SPEC.md` §4 lists seven; trimming that list before Phase 1 removes real work from WP-1.5, WP-5.2, WP-7.1, and WP-7.4.
- Is there an existing vulnerability scanner whose output format should drive WP-7.6, or should all three importers be built?
