# STATUS.md — NetShield

> Single source of truth for where the build is. Updated by the agent at the end of every package. If this file is wrong, the next session starts wrong.

## Current WP

**WP-0.5 — RBAC and audit log** `[SENSITIVE]`
Branch: `feat/wp-0.5-rbac-audit`
Phase: 0 — Foundation
Sensitive: yes — the human reviews this package line by line.

## Phase progress

| Phase | Packages | Done | State |
|---|---|---|---|
| 0 — Foundation | 7 | 4 | In progress |
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

- **WP-0.3 — Platform primitives** (2026-09-03, `feat/wp-0.3-platform`). `NetShield.Platform` now holds the seven primitives every later package builds on. `Result`/`Result<T>` with a typed `Error` and an `ErrorKind`-to-status-code table, mapped to HTTP by `ResultEndpointExtensions`. RFC 9457 problem details for both a handled failure and an unhandled exception, carrying `traceId` and never a stack trace, an exception message or a credential — registered for every environment, not just Production. Cursor pagination: `Cursor` encode/decode, `PageRequest` (default 50, max 200, over-max rejected rather than clamped) and `ToCursorPage`, returning the `CursorPage<T>` shape from `Contracts`. `IClock` over `TimeProvider`, normalised to UTC at the boundary. Secret redaction applied by replacing `ILoggerFactory`, so every provider — console, OpenTelemetry, and anything a later package adds — is covered without a call site remembering. The transactional outbox: `outbox_messages` (migration `Platform_AddOutbox`), `IEventBus` writing the row into the caller's transaction, `OutboxProcessor` delivering a batch, and `OutboxDispatcher` looping with backoff. `NetShield.ArchitectureTests` gained the `ARCHITECTURE.md` §4 module rules over the project-reference graph. 118 tests: 94 unit, 10 integration against Testcontainers PostgreSQL on the AppHost's TimescaleDB image, 14 architecture.

- **WP-0.4 — Identity domain and local authentication** (2026-09-03, `feat/wp-0.4-identity`). `NetShield.Identity` holds `users` and `refresh_tokens` (migration `Identity_AddUsersAndRefreshTokens`, its own history table `__ef_migrations_history_identity`). Argon2id hashing at the OWASP parameters, stored as a PHC string so the costs travel with the hash and a sign-in can rehash at the current work factor. A configurable password policy applied wherever a password is set, the seeder included. Cookie authentication with a short session cookie on `/` and a `Path`-scoped refresh cookie, both `HttpOnly`, `Secure`, `SameSite=Lax`, set in code and not bindable. Rotating refresh tokens stored only as a SHA-256 digest, with chain revocation on replay. Lockout after five consecutive failures for fifteen minutes. `POST /api/v1/auth/{login,refresh,logout,password}` and `GET /api/v1/auth/me`, FluentValidation at the boundary, a source-generated serialiser that deliberately cannot serialise a grant. A first-run administrator seeded only into an empty `users` table, from an Aspire-generated persisted secret parameter, forced to change its password. 220 tests: 148 unit, 57 integration against Testcontainers PostgreSQL, 15 architecture.

## In flight / noticed

Things spotted during a package that are out of its scope. Do not fix them in the current package — record them here and address them in a package of their own.

- **Nothing enforces `must_change_password` globally.** WP-0.4 sets the flag, returns it from every authentication endpoint, and clears it on a password change; it does not refuse other endpoints while it is set, because there are no other endpoints yet and the refusal belongs with the authorization pipeline. WP-0.5 has to add it as an authorization requirement, and WP-0.7 has to send the user to the change screen.
- **There is no user administration.** Nothing creates, disables, renames or resets a second account — the seeded administrator is the only account the system can produce. Whichever package owns the Administration screen in Phase 8 needs user CRUD, an administrator-initiated password reset that sets `must_change_password`, and a way back in when the sole administrator is locked out.
- **Sign-in is not rate limited.** Lockout bounds attempts against one account; nothing bounds attempts across many accounts from one source, and nothing spends Redis on it yet. Worth a look when the rate-limiting story is written, since the Argon2id verification on every attempt is itself the expensive part.
- No authentication event reaches the outbox. `UserSignedIn`, `AccountLockedOut` and `PasswordChanged` are the obvious candidates once WP-0.5 gives them somewhere to be recorded.
- `Identity:Seed:Password` reaches `Web.Host` as an environment variable from an Aspire parameter persisted to the AppHost's user secrets. Deployment has no equivalent yet; the package that writes `docker-compose.yml` has to decide how a mounted secret gets there, and the same question covers the credential KEK in WP-1.2.
- The Argon2id work factor is a single global setting. Raising it rehashes each account on its next sign-in and never on its own, so an account that has not signed in since a change keeps the older cost until it does.
- Lab environment is not yet stood up. Containerlab plus an `snmpsim` fixture corpus is needed before WP-1.5. See `WORKFLOW.md` § Test data.
- `docker-compose.yml` is in the `CONVENTIONS.md` §1 layout and `ARCHITECTURE.md` §2 names Docker Compose as the deployment orchestrator, but no Phase 0 package creates it. It needs a package of its own, most naturally in Phase 8 alongside hardening.
- **`dotnet test` now needs a Docker daemon** for `NetShield.IntegrationTests` alone. WP-0.3 gave that project its first tests and the Testcontainers dependency `CONVENTIONS.md` §7 requires; unit and architecture tests stay container-free, so a layer can still be run on its own. The WP-0.2 "Done when" criteria — dashboard health, `/health`, `/health/ready`, and the `timescaledb` extension version — are still verified by hand rather than by a test, and the harness to automate them now exists.
- `WORK_PACKAGES.md` WP-0.2 names **MailHog**; the build uses **MailPit** (`axllent/mailpit:v1.31.0`) instead. MailHog was archived upstream in 2024 at v1.0.1. MailPit is the maintained drop-in with the same SMTP-sink and web-UI role, and it ships a `/readyz` endpoint that lets the dashboard report the resource healthy. The WP text has not been edited; this note is the record of the deviation.
- `ServiceDefaults.MapDefaultEndpoints` maps the health endpoints in the Development environment only, which is the Aspire template's default and the reason a `dotnet run` outside Aspire answers 404 there. A Docker Compose deployment will need container health checks against these paths, so the package that writes `docker-compose.yml` has to decide deliberately whether to expose them in production and on which interface.
- `NetShield.UnitTests` reaches the Postgres container's resolved environment through `GetEnvironmentVariableValuesAsync`, which Aspire 13.5.3 marks obsolete; its replacement, `ExecutionConfigurationBuilder`, is not public in that version. The call is behind a scoped `#pragma` in `AppHostModel`. Revisit when Aspire makes the replacement public.
- The AppHost pins `timescale/timescaledb:2.29.0-pg17` and `axllent/mailpit:v1.31.0` by literal; Redis floats on the Aspire default (8.6 today). Container image versions have no equivalent of `Directory.Packages.props` and nothing yet updates them.
- The `ARCHITECTURE.md` §4 module rules are enforced over the **project-reference graph**, not over loaded assemblies, because a reference an empty project declares is elided from assembly metadata and a reflection rule would pass over all ten modules vacuously. Two §4 rules are therefore still unenforced and need an assembly-level rule library (NetArchTest or ArchUnitNET) in WP-1.1, once a module holds real types: *cross-module communication carries `Contracts` types only*, and *no module exposes an EF entity across its boundary*.
- **Nothing applies migrations at run time.** WP-0.3 ships `Platform_AddOutbox` and WP-0.4 ships `Identity_AddUsersAndRefreshTokens`, each applied explicitly in its integration-test harness; `Web.Host` does not migrate on startup, by decision. Until a migration step exists, a freshly started stack has neither table: the outbox dispatcher logs one error then backs off to a warning a minute, and the first-run seeder reports the missing `users` table and lets the host start rather than taking the API down with it — so **`aspire run` against an unmigrated database comes up healthy but has no administrator to sign in as, and every authentication endpoint answers 500.** The manual migration step is in the WP-0.4 verification checklist. WP-1.1 is the first package that cannot proceed without settling this — a dedicated migration process or job, not a startup hook. Two contexts now need applying, so whatever settles it has to apply both.
- `IntegrationEventRegistry` keys an outbox row by the event type's `FullName`, so moving or renaming an event type is a breaking change for rows still in flight. Fine while nothing is deployed; revisit before the first release if event types look likely to move.
- The outbox dispatcher assumes it is the only one running — it claims rows with a plain query rather than `FOR UPDATE SKIP LOCKED`. Correct for the single-node V1 in `SPEC.md` §3, and the reason `AddOutboxDispatcher` is a separate opt-in call that only `Web.Host` makes. A second dispatching process would double-deliver.
- Delivered outbox rows are kept, and nothing prunes them. They belong in the retention policy table `ARCHITECTURE.md` §3 describes, which arrives with the policy work in Phase 8.
- `OutboxPayload` uses reflection-based `System.Text.Json` rather than a source-generated context. `CONVENTIONS.md` §4 requires source generation for the API's JSON; these rows are internal and never leave the process, but the choice would have to be revisited if a NetShield process were ever published trimmed or AOT.
- `.editorconfig` carries placeholder sections for `*.py` and `*.{ts,tsx,...}` ahead of the collector (WP-1.3) and the SPA (WP-0.6). Revisit them when those toolchains land — `ruff` and Prettier own formatting there, not `dotnet format`.
- `Directory.Packages.props` now sets `CentralPackageTransitivePinningEnabled`, which surfaced two existing transitive downgrades and moved `Microsoft.Extensions.Hosting` from 10.0.0 to 10.0.11 to clear them. Every EF Core package is pinned at 10.0.11 and the local `dotnet-ef` tool was updated to match; a future `aspire update` has to keep the two in step.

## Blocked

_Nothing blocked._

## Open questions for the human

- Which vendors are actually present in the target estate? `SPEC.md` §4 lists seven; trimming that list before Phase 1 removes real work from WP-1.5, WP-5.2, WP-7.1, and WP-7.4.
- Is there an existing vulnerability scanner whose output format should drive WP-7.6, or should all three importers be built?
