# CONVENTIONS.md — NetShield

> Binding. Code that violates this document is not done, regardless of whether it works.

## 1. Repository layout

```
netshield/
  CLAUDE.md                    Agent entry point. Root only.
  NetShield.sln
  Directory.Build.props        Shared MSBuild properties. Version pins live here.
  Directory.Packages.props     Central package management. All versions.
  .editorconfig  .gitignore  .dockerignore
  docker-compose.yml
  .claude/settings.json
  docs/
    SESSION.md STATUS.md WORK_PACKAGES.md WORKFLOW.md
    ARCHITECTURE.md CONVENTIONS.md DESIGN.md DECISIONS.md
    SPEC.md ROADMAP.md
    design/reference-dashboard.png
  src/    (layout per ARCHITECTURE.md §4)
  tests/
```

## 2. C#

- `net10.0`, nullable enabled, implicit usings enabled, `TreatWarningsAsErrors=true`. All three set in `Directory.Build.props`, never per-project.
- File-scoped namespaces. One public type per file, file named for the type.
- `sealed` by default on classes. `record` for DTOs and value types, `class` for entities and services.
- `var` only when the right-hand side names the type.
- Async all the way down. No `.Result`, no `.Wait()`, no `async void` outside event handlers. Every async method takes a `CancellationToken` and passes it on.
- Minimal APIs grouped per module in an `Endpoints/` folder, one file per resource, registered by a single `Map{Module}Endpoints(this IEndpointRouteBuilder)` extension.
- No exceptions for control flow. Handlers return `Result<T>`; the endpoint layer maps `Result` to status codes. Exceptions mean a bug or an infrastructure failure.
- Central package management only. A `PackageReference` with a `Version` attribute in a `.csproj` is a bug.

**Namespaces:** `NetShield.{Module}.{Feature}` — e.g. `NetShield.Inventory.Discovery`.

**File naming:**
- Endpoints: `DeviceEndpoints.cs`
- Handlers: `CreateDeviceHandler.cs`, `GetDeviceListHandler.cs`
- Entities: `Device.cs`
- DTOs (in `Contracts`): `DeviceSummary.cs`, `CreateDeviceRequest.cs`
- EF config: `DeviceConfiguration.cs`
- Migrations: `dotnet ef migrations add {Module}_{Verb}{Noun}` → `Inventory_AddDeviceCriticality`

## 3. Database

- `snake_case` for tables and columns. Plural table names: `devices`, `alert_rules`, `flow_records`.
- Every table: `id` (`uuid`, generated v7 for time-ordering), `created_at`, `updated_at` (`timestamptz`, always UTC).
- Timestamps are `timestamptz` and stored UTC. Never `timestamp`. Never a local time in the database.
- Foreign keys named `{table_singular}_id`. Every FK has an index.
- Soft delete via `deleted_at` on inventory tables only. Telemetry, flow, and log tables are immutable — insert only, removal only by retention policy.
- Migrations are per-module, forward-only, and never edited after merge. A mistake gets a new migration.
- Hypertables (`metric_samples`, `flow_records`, `log_events`) declare their chunk interval, compression policy, and retention policy in the migration that creates them.
- No stored procedures. No triggers except the `audit_log` append-only rule and `updated_at` maintenance.

## 4. API

- REST, versioned under `/api/v1/`. Kebab-case paths: `/api/v1/alert-rules`.
- JSON, `camelCase`, `System.Text.Json` source-generated contexts.
- Every list endpoint: cursor pagination (`?cursor=&limit=`, default 50, max 200), returning `{ items, nextCursor, totalCount? }`. Offset pagination is not used.
- Errors are RFC 9457 `application/problem+json`. `traceId` in every problem response. Never leak an exception message, a stack trace, a SQL fragment, or a credential.
- Standard codes: `200` read, `201` + `Location` create, `204` delete, `400` validation, `401` unauthenticated, `403` unauthorized, `404` not found, `409` conflict, `422` semantic rejection, `429` rate limited.
- Validation with FluentValidation at the endpoint boundary. A handler assumes valid input.
- OpenAPI generated at build. The TypeScript client is regenerated in the same package that changes the contract — a drifted client is a failing build.

## 5. Python collector

- Python 3.13+, `uv` for everything. `pyproject.toml`, `uv.lock` committed.
- `ruff` for lint and format (line length 100). `mypy --strict`. Both clean, or the package is not done.
- Full type annotations. No bare `except`. No `print` — `structlog` to stdout as JSON.
- `httpx.AsyncClient` for the API contract, with retry and jittered backoff.
- One module per protocol: `collector/snmp/`, `collector/icmp/`, `collector/ssh/`, `collector/discovery/`.
- Every device interaction has an explicit timeout. A hung session must never wedge the worker pool.
- Vendor quirks live in `collector/vendors/{vendor}.py` behind a common `VendorAdapter` protocol. No vendor `if` chains in shared code.

## 6. Frontend

- TypeScript strict, no `any`, no non-null assertion (`!`) except immediately after a runtime guard.
- Function components only. Named exports. One component per file.
- Files: `PascalCase.tsx` for components, `camelCase.ts` for hooks and utilities, `kebab-case` for route files.
- Structure: `src/features/{feature}/` holding `components/`, `hooks/`, `api/`. Shared primitives in `src/components/ui/`. Nothing shared lives in a feature folder.
- TanStack Query keys are constructed by a factory per feature, never inline string arrays.
- Tailwind utilities only. No CSS modules, no styled-components, no inline `style` objects except computed geometry. Design tokens come from the Tailwind theme defined in `docs/DESIGN.md` — never a raw hex in a class name.
- Every interactive element is keyboard reachable with a visible focus ring. Every icon-only control has an `aria-label`. Charts carry a text summary for screen readers.
- Loading states are skeletons matching the final layout, never a centered spinner on a full page. Empty states say what to do next. Error states say what failed and offer a retry.

## 7. Testing

- **A package with no tests is not done.**
- Unit tests: xUnit + FluentAssertions + NSubstitute. Naming: `MethodName_Scenario_ExpectedResult`.
- Integration tests: Testcontainers with real PostgreSQL + TimescaleDB. No in-memory provider, ever — it hides the behavior that matters.
- Architecture tests enforce ARCHITECTURE.md §4 module rules and the audit-log append-only rule.
- Frontend: Vitest + React Testing Library. Test behavior through the DOM, never implementation internals. MSW for API mocking.
- Python: pytest with `pytest-asyncio`. Protocol interactions tested against recorded fixtures, never a live device.
- Required coverage by area: rule evaluation, enrichment and asset-resolution logic, RBAC checks, credential handling, and retention policy — all need tests before the package closes. CRUD scaffolding needs a happy path and one failure path.

## 8. Logging and observability

- `ILogger<T>` with structured properties. No string interpolation into log messages.
- Levels: `Error` needs a human. `Warning` is degraded but self-healing. `Information` is a business event. `Debug` is off in production.
- Every request carries a correlation ID, propagated to the collector and back.
- OpenTelemetry traces and metrics via `ServiceDefaults`. New long-running work gets a span.
- Never log: credentials, session tokens, SNMP communities, private keys, full config file bodies, or raw syslog payloads at `Information` or above.

## 9. Git

- Branch: `feat/wp-X.Y-short-name`. One package, one branch.
- Commit: `type(scope): description (WP-X.Y)` — `feat`, `fix`, `chore`, `docs`, `test`, `refactor`.
- Squash merge to `main`. `main` is always green and always deployable.
- Never commit: `.env`, `*.pem`, `*.key`, real device credentials, real capture data, customer config backups.

## 10. Commands

```bash
aspire run                                        # everything, from repo root
dotnet build && dotnet test                       # backend
dotnet format --verify-no-changes                 # style gate
npm test --prefix src/NetShield.Web.Client        # frontend
uv run ruff check . && uv run mypy . && uv run pytest   # collector (from src/netshield-collector)
```
