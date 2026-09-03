# WORK_PACKAGES.md — NetShield

## How a session works

- **One package per session.** A session builds exactly one work package, then stops. Never carry a session across a phase boundary.
- **Continuity lives in the steering files, not in chat history.** `STATUS.md` says where the build is; `DECISIONS.md` says what has already been settled. A fresh session with no memory of the last one must be able to continue correctly. If that stops being true, the steering files are wrong and fixing them is the priority.
- **Lifecycle:** branch → new session → scope gate → build → verify → append `DECISIONS.md` → stop → human reviews, merges, tags.

## Package protocol

Every session follows this, without being reminded:

1. Load `ARCHITECTURE.md`, `CONVENTIONS.md`, `DESIGN.md` (if the package touches UI), `SPEC.md`, `STATUS.md`, `DECISIONS.md`, and this file's entry for the current WP.
2. **State the scope summary and stop.** List what will be built, what will not, which files will be created or changed, and any dependency that needs approval. Wait for "go". Do not write code before "go".
3. Build only what the package specifies. Something out of scope but worth doing goes into `STATUS.md` under *In flight / noticed*, not into the diff.
4. Finish with the **Package Completion Report**: what was built, files added/changed, decisions made, dependencies added and why, anything deferred, the regression command to run, and a numbered **manual verification checklist** the human can walk through in the browser.
5. Update `STATUS.md` (mark this WP done, point Current WP at the next one with its branch name) and append any decisions to `DECISIONS.md` as one-liners: "chose X over Y because Z (WP-N.N, date)". If no decisions were made, say so explicitly.
6. **Then stop.** Do not start the next package. The human verifies, commits, merges, and opens the next session.

**Never modify without an explicit instruction in the current WP:** auth configuration, credential handling and the encryption envelope, the outbox/bus wiring, the collector job contract, migration history from earlier packages, the `audit_log` table, or `DESIGN.md` tokens.

**Never, in any package:** write to a network device, implement SNMP `set`, add an update or delete path for `audit_log`, make an outbound internet call at runtime, commit a secret, or build anything from the Defer column of `docs/SPEC.md`.

**`[SENSITIVE]`** marks packages the human reviews line by line: anything touching auth, RBAC, credentials, encryption, the collector contract, audit integrity, or device access.

---

# Phase 0 — Foundation

### WP-0.1 — Solution skeleton
Create `NetShield.sln` targeting `net10.0` with the project layout from `CONVENTIONS.md` §1: AppHost, ServiceDefaults, Contracts, Platform, Web.Host, Ingest, ten empty module projects, four test projects. `Directory.Build.props` carries nullable, implicit usings, warnings-as-errors. `Directory.Packages.props` enables central package management. `.gitignore`, `.editorconfig`, `.dockerignore`.
**Done when:** `dotnet build` succeeds, `dotnet test` runs (zero tests is fine), no project targets anything but `net10.0`, no `PackageReference` carries a `Version` attribute.

### WP-0.2 — Aspire orchestration
AppHost wiring PostgreSQL 17 with the TimescaleDB image, Redis, and MailHog, all with persistent volumes. Web.Host registered with health endpoints and OpenTelemetry via ServiceDefaults. Connection strings flow from Aspire; none hardcoded anywhere.
**Done when:** `aspire run` brings up the dashboard with every resource healthy, the API answers `/health` and `/health/ready`, and `SELECT extversion FROM pg_extension WHERE extname='timescaledb'` returns a version.

### WP-0.3 — Platform primitives
In `NetShield.Platform`: `Result<T>` and its endpoint mapping, RFC 9457 problem-details handler with `traceId`, cursor pagination helpers, `IClock`, structured-logging setup with a secret-redaction processor, and the transactional outbox (table, dispatcher, `IEventBus`). Architecture tests project scaffolded with the module-reference rules from `ARCHITECTURE.md` §4.
**Done when:** architecture tests fail if a module references another module, an unhandled exception returns problem-details with no stack trace, an outbox round-trip is covered by an integration test, and a log line containing a password field is emitted redacted.

### WP-0.4 — Identity domain and local authentication `[SENSITIVE]`
`NetShield.Identity`: user entity, Argon2id password hashing, login/logout/refresh, cookie session (`HttpOnly`, `Secure`, `SameSite=Lax`) with rotating refresh tokens, lockout after repeated failures, password policy. Seeded first-run administrator with a forced password change.
**Done when:** login succeeds and sets the cookie, a wrong password returns 401 with no user enumeration, five failures lock the account, refresh rotates and invalidates the prior token, and no password or token appears in any log at any level.

### WP-0.5 — RBAC and audit log `[SENSITIVE]`
Roles (Administrator, Operator, Analyst, Read-only) and a permission set. Endpoint-level authorization plus a module-level resource check helper. `audit_log` as an append-only hypertable-free table with a database rule blocking `UPDATE` and `DELETE`, an architecture test asserting no update/delete path exists in code, and automatic recording of actor, source IP, action, target, and before/after for every state-changing call.
**Done when:** an Analyst is refused a write with 403, the audit row exists for every successful write, `UPDATE audit_log` fails at the database, and the architecture test fails if someone adds a delete method.

### WP-0.6 — SPA shell
React 19 + Vite + TypeScript strict. Tailwind theme built from every token in `DESIGN.md` §3–4, Inter self-hosted as woff2. TanStack Router with the full route tree matching the sidebar in the reference screenshot. Sidebar (expanded/collapsed, active state, expandable sections) and header (search field with ⌘K chip, notification bell, help, theme toggle, user block) built to spec. TanStack Query provider, generated OpenAPI client wired, MSW set up for tests.
**Done when:** every sidebar route renders a placeholder page, the sidebar collapses and persists, the layout matches the reference at 1536px, no raw hex appears in any component, and the a11y check passes with visible focus on every control.

### WP-0.7 — Authentication UI and session handling `[SENSITIVE]`
Login page, forced password change, session expiry handling with silent refresh, logout, protected route guard, and role-aware nav hiding. 401 anywhere redirects to login preserving the return path.
**Done when:** an unauthenticated visit to any route lands on login, a successful login returns to the requested route, an expired session refreshes silently once and then redirects, and a Read-only user sees no write controls.

---

# Phase 1 — Inventory and discovery

### WP-1.1 — Device domain and CRUD
Device entity (hostname, primary IP, vendor, model, OS version, serial, site, role, criticality tier, environment, owner, tags, notes, state, soft delete), migrations, validation, cursor-paginated list with filter and sort, detail, create, update, delete. Outbox events `DeviceCreated`, `DeviceUpdated`, `DeviceRemoved`.
**Done when:** full CRUD passes integration tests against Testcontainers PostgreSQL, duplicate primary IP returns 409, list pagination is stable across inserts, and every mutation writes an audit row.

### WP-1.2 — Credential profiles and envelope encryption `[SENSITIVE]`
Credential profile entity supporting SNMP v2c community, SNMP v3 (auth/priv), and SSH (password or key). Envelope encryption with a configuration-supplied key-encryption key, key rotation path, and a decrypt path callable only from the collector-job endpoint. Profiles reference devices many-to-many. Secret values are write-only over the API — never returned, never logged, never rendered.
**Done when:** a stored secret is unreadable in the database without the KEK, the API never returns a secret value in any response shape, rotation re-wraps without downtime, and a test asserts no secret field is serializable.

### WP-1.3 — Collector service skeleton and job contract `[SENSITIVE]`
`netshield-collector` Python project with `uv`, ruff, mypy strict, pytest, structlog. The three internal endpoints from `ARCHITECTURE.md` §7 with shared-secret auth. Job lease model with visibility timeout and idempotent result submission. Heartbeat with version and capacity. `VendorAdapter` protocol defined with no vendor implementations yet.
**Done when:** the collector leases a job, submits a result, and heartbeats; a duplicate result submission is a no-op; leases expire and re-queue; the collector holds no database credential; ruff, mypy, and pytest are clean.

### WP-1.4 — ICMP reachability and device state
Collector ICMP job with configurable count, timeout, and interval. Device state machine (Online / Warning / Offline / Unknown) driven by consecutive success and failure thresholds, with RTT recorded. State transitions raise outbox events.
**Done when:** a device that stops responding transitions to Offline after the configured threshold and back to Online on recovery, RTT is recorded per probe, and a flapping device does not emit a transition per probe.

### WP-1.5 — SNMP walk discovery and fingerprinting
Collector SNMP job walking system, interface, entity, and vendor-specific MIBs. Fingerprint resolution to vendor, model, OS version, serial, and uptime for the seven supported vendors in `SPEC.md` §4, with generic SNMP fallback. Interface inventory per device.
**Done when:** a walk against recorded fixtures for each supported vendor produces the correct fingerprint, an unrecognized device lands as generic SNMP with reduced capability flagged, and a timeout on one device does not stall the batch.

### WP-1.6 — Discovery jobs and scheduling
Seed configuration (CIDR ranges, exclusions, credential profile order), scheduled and on-demand discovery runs, run history with per-host outcome, promotion of discovered hosts to devices with a review step, and a permanent ignore list.
**Done when:** a run over a /24 completes within the configured window, results appear as reviewable candidates rather than auto-created devices, an ignored host never reappears, and a re-run updates rather than duplicates.

### WP-1.7 — Devices and Clients UI
Devices list (virtualized table, filters for state/vendor/site/criticality, health badges per `DESIGN.md` §6), device detail with tabs, add/edit forms, credential profile assignment, and the discovery run view. Clients list with its own filters.
**Done when:** the table matches the reference styling, 500 devices scroll smoothly, filters compose and survive a refresh via URL state, and empty and error states follow `DESIGN.md` §8.

### WP-1.8 — Client tracking and time-accurate IP resolution
Client entity from ARP tables, MAC address tables, DHCP leases, and wireless associations. History of IP and port assignment as closed time intervals. The `ResolveAssetAt(ip, timestamp)` service backing all downstream enrichment, cached in Redis and invalidated on inventory change.
**Done when:** an IP reassigned between two hosts resolves to the correct host for a timestamp on either side of the handover, resolution is under 1 ms warm at 5,000 clients, and a cold cache rebuilds from PostgreSQL without a gap.

---

# Phase 2 — Topology

### WP-2.1 — Neighbor collection and adjacency
LLDP and CDP neighbor collection per vendor, plus routing-table read for L3 adjacency. Adjacency table with discovered-at and last-seen, and edge confidence when sources disagree.
**Done when:** a three-switch fixture topology produces the correct edge set, a removed link ages out, and conflicting LLDP/CDP data resolves deterministically.

### WP-2.2 — VLAN inventory
VLAN entity per device with name, ID, member ports, and derived device and client counts.
**Done when:** VLAN counts on a fixture estate match the reference dashboard's VLAN tiles, and a VLAN present on multiple switches appears once with aggregated membership.

### WP-2.3 — Topology graph API
Graph endpoint returning nodes and edges with state, filterable by site, VLAN, and depth from a root. Server-side `dagre` layout hint. Response bounded and paginated by subgraph.
**Done when:** a 500-node estate returns in under 500 ms, filters reduce the result correctly, and an unreachable segment is returned as a disconnected component rather than dropped.

### WP-2.4 — Topology UI
React Flow canvas per `DESIGN.md` §6: dot grid, 48px node tiles with state-encoded borders, zoom/fit controls stacked top-left, legend in the card header, node click opening device detail.
**Done when:** the canvas visually matches the reference topology card, 500 nodes pan and zoom at 60fps, and the canvas is keyboard navigable with a table fallback.

---

# Phase 3 — Telemetry

### WP-3.1 — Time-series schema
`metric_samples` hypertable with chunk interval, compression after 7 days, and a retention policy read from the policy table. Metric definition registry (name, unit, type, aggregation). Continuous aggregates at 5-minute and 1-hour granularity.
**Done when:** a 30-day synthetic load compresses as configured, retention drops chunks past the window, continuous aggregates refresh on schedule, and a rollup query over 90 days returns in under 200 ms.

### WP-3.2 — SNMP metric polling
Collector polling job for interface counters (in/out octets, errors, discards, operational state), CPU, memory, temperature, PSU state, and optics DOM where supported. Counter wrap and reset handling, per-device polling interval, and jittered scheduling to avoid thundering herd.
**Done when:** a 64-bit counter wrap produces no negative rate, a device reboot resets rather than spikes, 500 devices poll within a 60-second interval, and per-vendor OID maps are fixture-tested.

### WP-3.3 — Metric ingest and batch writer
Ingest contract from collector results into `metric_samples` with batched `COPY`, duplicate suppression by (device, metric, timestamp), and ingest lag metrics per source.
**Done when:** 20,000 samples/sec sustain without lag growth, a replayed batch produces no duplicates, and the writer surfaces its queue depth as a metric.

### WP-3.4 — Series query API
Time-range series endpoints with automatic granularity selection, multi-series comparison, downsampling, and unit conversion (bps/Kbps/Mbps/Gbps/Tbps).
**Done when:** a 24-hour interface query returns the aggregate not the raw rows, granularity switches at documented thresholds, and unit selection matches the Gbps dropdown in the reference bandwidth card.

### WP-3.5 — Device health rollup
Composite health per device from reachability, interface error rate, resource utilization, and environmental state, producing Healthy / Warning / Critical with the contributing reason. Estate-wide rollup for the Network Status KPI.
**Done when:** health degrades on a threshold breach and recovers on clearance, the reason string names the specific contributor, and the estate rollup matches the reference "Healthy / All systems operational" tile shape.

### WP-3.6 — Telemetry UI
Device Health card (list with badges and relative timestamps), interface utilization charts on device detail, and the KPI strip cards with sparklines per `DESIGN.md` §6.
**Done when:** the KPI strip visually matches the reference including sparkline bleed and delta lines, charts animate once on mount and not on refetch, and every chart has a text summary.

---

# Phase 4 — Flows

### WP-4.1 — NetFlow v9 and IPFIX collector
`NetShield.Ingest` UDP receiver with template caching per exporter, option-template handling, bounded disk-spilling channel, and per-exporter statistics.
**Done when:** v9 and IPFIX fixtures from three vendors decode correctly, a template arriving after its data records is handled by buffering, 20,000 flows/sec sustain, and a full channel raises an alert rather than dropping silently.

### WP-4.2 — Flow enrichment and storage
`flow_records` hypertable. Enrichment attaching source and destination asset, client, VLAN, site, and direction using `ResolveAssetAt` at the flow's timestamp. Enrichment failure writes the record flagged, never blocks.
**Done when:** enrichment resolves correctly across a DHCP handover, an unresolvable address lands flagged, and enrichment adds under 10% to write latency.

### WP-4.3 — Application identification
Application resolution by port/protocol table, plus a maintainable signature set for the applications shown in the reference card. User-editable custom application definitions. Everything unmatched rolls into "Others".
**Done when:** the built-in table classifies common traffic correctly, a custom definition takes precedence, and reclassification does not require reprocessing historical rows.

### WP-4.4 — Flow aggregation API
Top talkers, top applications, top conversations, and per-interface bandwidth over a time range, with continuous aggregates backing the common windows.
**Done when:** a top-N query over 24 hours returns in under 500 ms at target scale, percentages sum correctly with "Others", and results are consistent between the raw and aggregate paths.

### WP-4.5 — Bandwidth and Top Applications UI
The Bandwidth Utilization area chart (inbound/outbound, unit dropdown, tooltip per `DESIGN.md` §6) and the Top Applications donut with its right-hand legend list.
**Done when:** both cards match the reference exactly including legend layout and the centered donut total, and the unit dropdown re-scales axis and values together.

---

# Phase 5 — Logs

### WP-5.1 — Syslog receiver
UDP, TCP, and TLS listeners. RFC 3164 and RFC 5424 framing, octet-counting and non-transparent framing for TCP, per-source rate limiting, and the same bounded disk-spilling channel pattern.
**Done when:** malformed input never crashes a listener, 5,000 events/sec sustain, TLS requires a valid client connection, and a slow consumer applies back-pressure rather than dropping.

### WP-5.2 — Parsers and the normalized event schema
Normalized event schema in `Contracts`. Vendor parsers for the seven supported vendors mapping to it, with a raw-passthrough fallback that never loses the original message. Parser versioning so an improved parser does not invalidate stored events.
**Done when:** each vendor's fixture set parses to the expected normalized shape, an unparseable message stores with `parser=raw` and the full original, and parsers are hot-swappable without restart.

### WP-5.3 — Log storage, retention, and source health
`log_events` hypertable with compression and policy-driven retention. Per-source ingest statistics, expected-cadence learning, and **silent-source detection** raising an alert when a previously-chatty source goes quiet.
**Done when:** retention drops past-window chunks, a source silenced for its configured grace period raises an alert, and a newly added source does not alarm during its learning window.

### WP-5.4 — Log search API
Field filters, full-text search over the message body, time-range scoping, cursor pagination over the hot window, and a saved-search entity.
**Done when:** a filtered search over 90 days returns the first page in under 1 s, results are stable under concurrent ingest, and a saved search reproduces its result set.

### WP-5.5 — Logs UI
Log viewer with filter chips, live tail toggle, expandable rows showing the normalized fields and raw message, monospace rendering per `DESIGN.md` §4, and the source health panel.
**Done when:** live tail keeps up at 1,000 events/sec without freezing the tab, virtualization holds at 100,000 rows, and pausing tail preserves scroll position.

---

# Phase 6 — Alerting

### WP-6.1 — Alert rule domain and DSL
Rule entity with type (threshold, rate of change, absence, state change), target selector (device, group, tag, interface, source), condition, evaluation window, severity, and enablement. A readable text DSL with a parser, validator, and human-readable renderer.
**Done when:** each rule type round-trips through the DSL, an invalid rule is rejected with a message naming the position, and the renderer output is unambiguous.

### WP-6.2 — Rule evaluation engine
Scheduled evaluator over metrics, flows, logs, and inventory state. Hysteresis to prevent flapping, per-rule evaluation cost metrics, and a hard budget that skips and alarms rather than falling behind silently.
**Done when:** each rule type fires and clears correctly against a synthetic series, a flapping metric produces one alert not fifty, 500 rules evaluate within the interval, and an over-budget evaluation raises a platform alert.

### WP-6.3 — Incident deduplication and lifecycle
Alert instances grouped into incidents by rule and target with occurrence counts and first/last seen. Lifecycle: New → Acknowledged → Resolved, with assignment, notes, auto-resolve on clearance, and full audit.
**Done when:** repeated firings increment rather than multiply, acknowledgment survives a re-fire, auto-resolve triggers on clearance, and every transition is audited.

### WP-6.4 — Topology-aware suppression
When a device or link goes down, alerts for devices reachable only through it are suppressed and attached to the root incident as impacted assets.
**Done when:** a core switch failure produces one incident listing its downstream devices instead of N device-down alerts, suppression releases on recovery, and a device with a redundant path is not suppressed.

### WP-6.5 — Notification channels
Email (SMTP, MailHog in development) and generic webhook with HMAC signing. Routing by severity, tag, and time. Rate limiting, digest batching, retry with backoff, and a delivery log.
**Done when:** a High alert emails within 30 s, a webhook retries on 5xx and gives up cleanly, digest batching collapses a storm, and the delivery log shows every attempt.

### WP-6.6 — Alerts UI
Recent Alerts card per the reference (severity dot indicators, source, relative time, row menu), full alerts page with filters, incident detail with timeline and impacted assets, and bulk acknowledge.
**Done when:** the card matches the reference exactly, the Security Status donut severity split reconciles with the alerts list, and bulk actions are audited individually.

### WP-6.7 — Policies UI
Rule builder using the DSL with live validation and a preview against recent data, retention policy editor, notification routing editor, discovery schedule editor, and maintenance windows that suppress notification without suppressing recording.
**Done when:** a rule built in the UI evaluates identically to the same rule written in DSL, preview shows what would have fired over the last 24 hours, and a maintenance window suppresses notifications while alerts still record.

---

# Phase 7 — Configuration, compliance, vulnerabilities

### WP-7.1 — SSH configuration retrieval `[SENSITIVE]`
Collector SSH job using `scrapli` against an allowlist of `show`-class commands per vendor. No enable-mode writes, no configuration mode, no command outside the allowlist. Per-device timeout and concurrency limits.
**Done when:** each supported vendor's config retrieves against fixtures, a command outside the allowlist is rejected before dispatch, credentials never touch collector disk or logs, and a hung session is killed at timeout.

### WP-7.2 — Configuration versions and diff
Config blob storage with content hashing, version history, retention by count and age, and a structured diff with a side-by-side and unified view. Change detection raises `ConfigChanged` into the alert stream.
**Done when:** an unchanged config creates no new version, a change creates one with a correct diff, secrets in configs are masked in both storage and display, and `ConfigChanged` appears as an Info alert like the reference row.

### WP-7.3 — Golden templates and drift
Per-role golden config templates with variable substitution, drift assessment against them, and a drift severity model separating cosmetic from security-relevant differences.
**Done when:** an NTP-server difference scores lower than an ACL difference, drift results list the exact offending lines, and a device with no assigned role reports as unassessed rather than passing.

### WP-7.4 — Compliance baselines and rule DSL
Baseline entity holding rules in a readable DSL over parsed config (line presence, absence, regex match, value comparison, block scoping). Built-in CIS-style baselines for the supported vendors plus custom baseline authoring.
**Done when:** each built-in baseline evaluates correctly against pass and fail fixtures per vendor, a custom rule authored in the UI evaluates identically, and an inapplicable rule reports as not-applicable rather than failing.

### WP-7.5 — Compliance assessment and evidence
Scheduled and on-demand assessment runs, per-device and per-baseline pass/fail with the evidence line and its config version, exception handling with expiry and justification, and score trending.
**Done when:** an assessment run over 500 devices completes within its window, evidence links to the exact config version and line, an exception suppresses a finding until it expires, and the trend reflects historical runs.

### WP-7.6 — Vulnerability import and correlation
Importers for `.nessus`, OpenVAS XML, and a documented CSV shape. Correlation to inventory assets by IP, MAC, and hostname with a manual resolution queue for ambiguity. Prioritization combining CVSS, asset criticality, and an internet-facing flag. Remediation status tracking.
**Done when:** each format imports with correct field mapping, an unmatched finding lands in the resolution queue rather than being dropped, priority ranks a critical CVE on a Tier-1 exposed asset above the same CVE on an isolated one, and a re-import updates rather than duplicates.

### WP-7.7 — Configuration, Compliance, and Vulnerabilities UI
Config history and diff viewer, drift report, compliance dashboard with per-baseline scores and drill-down to evidence, exception management, and the vulnerability list with prioritization, filters, and remediation tracking.
**Done when:** the diff viewer renders a 5,000-line config without lag, compliance drill-down reaches the offending line in two clicks, and every page follows the card, table, and badge specs in `DESIGN.md` §6.

---

# Phase 8 — Dashboard, reporting, hardening

### WP-8.1 — Widget catalog and layout persistence
Widget registry with per-widget data contracts, sizes, and options. Per-user dashboard layout persisted server-side, drag to reorder, resize within the grid, add and remove, and a reset to default.
**Done when:** a layout survives logout and login on another browser, the Add Widget flow matches the reference button, and an unavailable data source degrades the widget rather than breaking the page.

### WP-8.2 — Overview dashboard
Assemble the reference dashboard exactly: the five KPI cards, Network Topology, Bandwidth Utilization, Security Status donut, Recent Alerts, Top Applications, Device Health. Global time-range selector applying to every widget.
**Done when:** a side-by-side against `docs/design/reference-dashboard.png` at 1536px shows no layout, spacing, color, or type deviation; every tile is fed by real data; and first contentful paint is under 1.5 s warm.

### WP-8.3 — Live updates
SignalR hub with topic subscriptions per route. Live push for alerts, device state, and dashboard tiles, with the 400ms value-flash from `DESIGN.md` §7. Reconnect with backoff and a stale-data indicator when disconnected.
**Done when:** an alert appears without a refresh, a dropped connection shows the stale indicator and recovers, and 50 concurrent sessions do not degrade API latency.

### WP-8.4 — Global search
⌘K palette searching devices, clients, alerts, and pages, with type-ahead, recent items, keyboard navigation, and permission filtering so results never leak what a role cannot see.
**Done when:** search returns in under 200 ms at target scale, keyboard navigation covers open and select without a mouse, and a Read-only user's results contain nothing they cannot open.

### WP-8.5 — Reporting engine
Report definitions for inventory, availability, bandwidth, compliance, vulnerability, and alert activity. PDF and CSV generation, scheduling, email delivery, and a generated-report archive with retention.
**Done when:** each report type generates with correct data for a chosen range, a scheduled report arrives by email, PDFs carry generation timestamp and range, and generation runs off the request thread.

### WP-8.6 — Administration and identity hardening `[SENSITIVE]`
User and role management UI, OIDC SSO with group-to-role mapping, TOTP MFA with recovery codes, session management with remote revoke, the audit log viewer with filters and export, and a system health page covering collector, ingest, database, and queue depth.
**Done when:** SSO login maps groups to roles correctly, MFA enrollment and recovery both work, an administrator can revoke another user's session immediately, the audit viewer is read-only with no delete affordance anywhere, and a broken SSO configuration still permits local administrator login.

### WP-8.7 — Deployment, backup, and documentation
Production `docker-compose.yml` for all five processes, container hardening (non-root, read-only root filesystem, minimal base), environment-variable configuration with a documented reference and startup validation, database backup and restore scripts covering hypertables and config blobs, an upgrade path, and operator documentation.
**Done when:** a clean host reaches a working login from `docker compose up` and a documented `.env`, a restore from backup reproduces a full working system including time-series data, a missing required variable fails startup with a clear message rather than a null reference, and no container runs as root.

---

## Bundling

Small adjacent packages may be bundled at kickoff by saying so explicitly — "do WP-2.1 and WP-2.2 together". Never bundle a `[SENSITIVE]` package with anything else, and never bundle across a phase boundary.
