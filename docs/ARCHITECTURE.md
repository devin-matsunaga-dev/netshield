# ARCHITECTURE.md — NetShield

> Binding. Every work package obeys this document verbatim. If a package appears to require deviating from it, the session stops and asks. Architecture changes happen deliberately between phases, never mid-package.

## 1. The Defer boundary is architectural

`docs/SPEC.md` §3 lists what V1 does not build. That list is enforced here, not just aspirational:

- **No component may write to a network device.** The collector holds read-only SNMP communities and CLI accounts scoped to `show`-class commands. There is no write path, no SNMP `set` code, and no config-push module. Do not create one, do not stub one, do not leave a `TODO: push config`.
- **No graph database, no event-correlation engine.** Relationships are foreign keys and adjacency tables in PostgreSQL. If a package seems to want multi-hop traversal, stop and ask.
- **No outbound internet calls at runtime.** No TI feeds, no CVE lookups against NVD, no telemetry-home. Vulnerability data arrives by file import only.

## 2. Runtime shape

Five processes, orchestrated by .NET Aspire in development and by Docker Compose in deployment.

| Process | Stack | Responsibility |
|---|---|---|
| `NetShield.AppHost` | .NET 10 Aspire | Dev orchestration only. Not deployed. |
| `NetShield.Web.Host` | .NET 10, ASP.NET Core | The API. Auth, RBAC, all read/query endpoints, all user-facing writes, rule evaluation, notification dispatch, report generation. Serves the SPA build in production. |
| `NetShield.Ingest` | .NET 10 worker | Push-based, high-throughput ingest only: syslog receiver (UDP/TCP/TLS) and NetFlow v9 / IPFIX collector. Parse, normalize, enrich, batch-write. |
| `netshield-collector` | Python 3.13, uv | Pull-based collection: ICMP, SNMP polling, SNMP walk discovery, SSH config retrieval. Scheduled by the API, results posted back over an internal HTTP contract. |
| `NetShield.Web.Client` | React 19 + Vite | The SPA. |

**Why the split.** Push ingest is throughput- and back-pressure-shaped work and belongs in the runtime with the better socket and channel primitives. Pull collection is protocol-library-shaped work and Python has by far the better SNMP and network-CLI ecosystem (`pysnmp`, `scrapli`, `netmiko`, `ncclient`). Neither owns business logic — both are dumb producers. All decisions live in the API.

## 3. Data stores

| Store | Use |
|---|---|
| **PostgreSQL 17 + TimescaleDB** | Everything. Relational tables for inventory, config, users, alerts, findings. Hypertables for metrics, flows, and log events. One database, one connection pool, one backup story. |
| **Redis** | Distributed cache, rate limiting, background-job coordination, SignalR backplane. Never a source of truth — the system must survive a full Redis flush with nothing worse than a cold cache. |
| **Filesystem (object path)** | Config backup blobs, generated reports, imported scanner files. Path configurable, defaults to a mounted volume. |

Hypertable policy: metrics, flows, and log events are Timescale hypertables with compression after 7 days and retention driven by the policy table, not by a hardcoded interval. Continuous aggregates provide the 5-minute and 1-hour rollups the dashboard reads — the dashboard never queries raw hypertable rows.

## 4. Backend structure

Modular monolith. Vertical slices, one project per module, no shared "Services" layer.

```
src/
  NetShield.AppHost/
  NetShield.ServiceDefaults/
  NetShield.Contracts/          DTOs, enums, the normalized event schema. No dependencies.
  NetShield.Platform/           Cross-cutting: auth, RBAC, audit, outbox, time, crypto, paging.
  NetShield.Web.Host/           Composition root. Endpoint mapping. SPA hosting.
  NetShield.Ingest/             Syslog + flow receivers.
  Modules/
    NetShield.Inventory/        Devices, credentials, discovery, clients, topology.
    NetShield.Telemetry/        Metric ingestion contract, series queries, health rollups.
    NetShield.Flows/            Flow query, enrichment, top-N aggregation.
    NetShield.Logs/             Event query, parsers, source health.
    NetShield.Alerting/         Rules, evaluation, incidents, notifications.
    NetShield.Configs/          Config backup, versions, diffs, drift.
    NetShield.Compliance/       Baselines, assessment, evidence.
    NetShield.Vulnerabilities/  Import, correlation, scoring.
    NetShield.Reporting/        Report definitions, generation, scheduling.
    NetShield.Identity/         Users, roles, sessions, SSO, MFA.
  netshield-collector/          Python.
  NetShield.Web.Client/         React.
tests/
  NetShield.UnitTests/
  NetShield.IntegrationTests/
  NetShield.ArchitectureTests/
  netshield-collector/tests/
```

**Module rules, enforced by `NetShield.ArchitectureTests`:**
- A module may reference `Contracts` and `Platform`. A module may **not** reference another module.
- Cross-module communication is one-way, asynchronous, via the in-process message bus, carrying `Contracts` types only.
- `Web.Host` may reference everything. Nothing may reference `Web.Host`.
- No module exposes an EF entity across its boundary. DTOs cross; entities do not.

## 5. Messaging and the outbox

Cross-module events go through an in-process bus backed by a transactional outbox table. Write the domain change and the outbox row in the same transaction; a dispatcher publishes after commit. This is how `DeviceDiscovered`, `MetricThresholdBreached`, `ConfigChanged`, and `AlertRaised` travel without module coupling.

The outbox wiring is written once in `NetShield.Platform` and is **never modified without an explicit instruction in the current work package**.

## 6. Ingest path

```
device → syslog/flow UDP socket
       → bounded channel (disk-spilling when full, never dropping silently)
       → parser (vendor-specific → normalized schema)
       → enricher (asset + client resolution at event timestamp, via a Redis-cached
                   lookup rebuilt from inventory on change)
       → batch writer (COPY into hypertable, 1–5 s or N-row windows)
```

Every stage exports a depth and lag metric. A full channel raises an alert; it does not drop and stay quiet. Enrichment failure never blocks the write — the event lands with a null asset reference and a flag.

## 7. Collector contract

The API owns scheduling. The collector asks for work and returns results:

- `GET /internal/collector/jobs` — leased batch of due jobs (poll, discover, config-fetch).
- `POST /internal/collector/results` — batched results, idempotent by job ID.
- `POST /internal/collector/heartbeat` — liveness, version, capacity.

Authenticated with a shared secret from configuration, bound to the internal network. The collector holds no database credentials and never touches PostgreSQL. Device credentials are fetched per-job, decrypted in the API, delivered over TLS, held in memory only, and never written to collector disk or logs.

## 8. Security architecture

- Credentials encrypted at rest with envelope encryption; the data-encryption key is wrapped by a key-encryption key supplied by configuration (env var, mounted file, or external KMS). Plaintext credentials exist only in process memory.
- Authentication: cookie session for the SPA (`HttpOnly`, `Secure`, `SameSite=Lax`) with rotating refresh; bearer tokens for the collector and API integrations. OIDC for SSO. TOTP MFA.
- Authorization: role plus permission claims checked at the endpoint, and again in the module for any resource-scoped operation. Never trust the client's claim of role.
- Audit: `audit_log` is append-only. **No update or delete path may ever be written for it** — enforced by an architecture test and a database rule.
- All secrets redacted at the logging sink by a redaction processor, not by developer discipline.

## 9. Frontend architecture

- React 19, Vite, TypeScript strict. Tailwind for styling.
- TanStack Query owns all server state. No server data in Zustand, Redux, or Context.
- TanStack Router, file-based routes mirroring the sidebar in `docs/design/reference-dashboard.png`.
- SignalR for live updates: alert stream, device state changes, dashboard tiles. One connection, topic subscriptions per route.
- Charts: Recharts. Topology: React Flow with a `dagre`-computed layout.
- The generated OpenAPI client is the only way the SPA talks to the API. No hand-written `fetch` calls to application endpoints.

## 10. Version floor

`net10.0` everywhere. Python `>=3.13`. Node 24 LTS. PostgreSQL 17 with TimescaleDB. No project targets anything else, no polyfill for an older runtime, no `<LangVersion>` override.
