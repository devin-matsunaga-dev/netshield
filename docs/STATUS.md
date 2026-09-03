# STATUS.md — NetShield

> Single source of truth for where the build is. Updated by the agent at the end of every package. If this file is wrong, the next session starts wrong.

## Current WP

**WP-0.3 — Platform primitives**
Branch: `feat/wp-0.3-platform`
Phase: 0 — Foundation
Sensitive: no

## Phase progress

| Phase | Packages | Done | State |
|---|---|---|---|
| 0 — Foundation | 7 | 2 | In progress |
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

- **WP-0.2 — Aspire orchestration** (2026-09-03, `feat/wp-0.2-aspire`). `NetShield.AppHost` orchestrates PostgreSQL 17 on `timescale/timescaledb:2.29.0-pg17`, Redis 8.6 with snapshotting, and MailPit, each on a named volume (`netshield-postgres-data`, `netshield-redis-data`, `netshield-mailpit-data`). `Web.Host` takes `AddServiceDefaults()` plus the Aspire Npgsql and Redis client integrations, waits for all three resources, and is probed at `/health/ready`. `Ingest` is registered with `AddServiceDefaults()` only. `ServiceDefaults` now maps `/health` (liveness) and `/health/ready` (readiness) in place of the template's `/health` and `/alive`. Twenty-two tests: ten over the AppHost resource model, five over the health endpoints, two repository-wide guards against a hardcoded connection string, and the five WP-0.1 invariants.

## In flight / noticed

Things spotted during a package that are out of its scope. Do not fix them in the current package — record them here and address them in a package of their own.

- Lab environment is not yet stood up. Containerlab plus an `snmpsim` fixture corpus is needed before WP-1.5. See `WORKFLOW.md` § Test data.
- `docker-compose.yml` is in the `CONVENTIONS.md` §1 layout and `ARCHITECTURE.md` §2 names Docker Compose as the deployment orchestrator, but no Phase 0 package creates it. It needs a package of its own, most naturally in Phase 8 alongside hardening.
- `NetShield.IntegrationTests` still holds no tests. WP-0.2 deliberately kept its suite container-free so `dotnet test` needs no Docker daemon; WP-0.3 gives the project its first tests and adds the Testcontainers dependency `CONVENTIONS.md` §7 requires. The WP-0.2 "Done when" criteria — dashboard health, `/health`, `/health/ready`, and the `timescaledb` extension version — were verified by hand, not by a test, and would be worth automating once that harness exists.
- `WORK_PACKAGES.md` WP-0.2 names **MailHog**; the build uses **MailPit** (`axllent/mailpit:v1.31.0`) instead. MailHog was archived upstream in 2024 at v1.0.1. MailPit is the maintained drop-in with the same SMTP-sink and web-UI role, and it ships a `/readyz` endpoint that lets the dashboard report the resource healthy. The WP text has not been edited; this note is the record of the deviation.
- `ServiceDefaults.MapDefaultEndpoints` maps the health endpoints in the Development environment only, which is the Aspire template's default and the reason a `dotnet run` outside Aspire answers 404 there. A Docker Compose deployment will need container health checks against these paths, so the package that writes `docker-compose.yml` has to decide deliberately whether to expose them in production and on which interface.
- `NetShield.UnitTests` reaches the Postgres container's resolved environment through `GetEnvironmentVariableValuesAsync`, which Aspire 13.5.3 marks obsolete; its replacement, `ExecutionConfigurationBuilder`, is not public in that version. The call is behind a scoped `#pragma` in `AppHostModel`. Revisit when Aspire makes the replacement public.
- The AppHost pins `timescale/timescaledb:2.29.0-pg17` and `axllent/mailpit:v1.31.0` by literal; Redis floats on the Aspire default (8.6 today). Container image versions have no equivalent of `Directory.Packages.props` and nothing yet updates them.
- `NetShield.ArchitectureTests` currently has no project references; its WP-0.1 tests read the `.csproj` files directly. WP-0.3 adds the module-reference rules from `ARCHITECTURE.md` §4, which will need assembly-level references and a rule library.
- `.editorconfig` carries placeholder sections for `*.py` and `*.{ts,tsx,...}` ahead of the collector (WP-1.3) and the SPA (WP-0.6). Revisit them when those toolchains land — `ruff` and Prettier own formatting there, not `dotnet format`.

## Blocked

_Nothing blocked._

## Open questions for the human

- Which vendors are actually present in the target estate? `SPEC.md` §4 lists seven; trimming that list before Phase 1 removes real work from WP-1.5, WP-5.2, WP-7.1, and WP-7.4.
- Is there an existing vulnerability scanner whose output format should drive WP-7.6, or should all three importers be built?
