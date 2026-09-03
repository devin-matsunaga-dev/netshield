# ROADMAP.md — NetShield

56 work packages across 9 phases. Phases ship in order; each ends with a gate that must pass before the next begins.

| Phase | Name | WPs | What exists at the end | Tag |
|---|---|---|---|---|
| 0 | Foundation | 7 | An authenticated, audited, empty app with the real shell | `v0.0-phase0` |
| 1 | Inventory & discovery | 8 | It knows what is on the network | `v0.1-phase1` |
| 2 | Topology | 4 | It knows how things connect | `v0.2-phase2` |
| 3 | Telemetry | 6 | It knows how things are doing | `v0.3-phase3` |
| 4 | Flows | 5 | It knows what the traffic is | `v0.4-phase4` |
| 5 | Logs | 5 | It knows what things are saying | `v0.5-phase5` |
| 6 | Alerting | 7 | It tells you when something is wrong | `v0.6-phase6` |
| 7 | Config, compliance, vulns | 7 | It knows what is misconfigured and exposed | `v0.7-phase7` |
| 8 | Dashboard, reporting, hardening | 7 | The product in the screenshot, deployable | `v1.0` |

## Why this order

Each phase is useful on its own and each is a hard dependency of the next. Inventory before everything, because asset resolution is what turns raw telemetry into information — this is exactly the layer most tools under-build and it is why they never quite deliver the single pane. Topology before alerting, because downstream-alert suppression needs the graph. Flows and logs before alerting, because rules need something to evaluate. The dashboard last, because a dashboard over empty tables teaches you nothing about whether it works.

Resist the temptation to build the pretty overview page early. It is the reward for finishing, not the starting point.

## 🏁 Phase gates

Run these before tagging. All must pass.

**Every gate, always:**
- `dotnet build && dotnet test` clean
- `dotnet format --verify-no-changes` clean
- `npm test --prefix src/NetShield.Web.Client` clean
- `uv run ruff check . && uv run mypy . && uv run pytest` clean, once the collector exists
- Architecture tests green, including the module-isolation and audit-append-only rules
- `STATUS.md` reflects reality; `DECISIONS.md` covers everything decided this phase
- `git diff` across the phase contains no device write path and no `audit_log` mutation
- `aspire update`, Dependabot reviewed, nothing in the version table past EOL

**🏁 Phase 0 —** A user can log in, MFA-less, and reach every route as a shell. An Analyst is refused a write with 403 and the refusal is audited. The sidebar and header match the reference at 1536px. No secret appears in any log.

**🏁 Phase 1 —** A discovery run over a lab /24 produces reviewable candidates, promotion creates devices, ICMP state transitions work, and `ResolveAssetAt` returns the correct host on both sides of a simulated DHCP handover. Credentials are unreadable in the database.

**🏁 Phase 2 —** The lab topology renders correctly on the canvas with accurate state colors, a removed link ages out, and 500 synthetic nodes pan at 60fps.

**🏁 Phase 3 —** 500 simulated devices poll within a 60-second interval, a counter wrap produces no negative rate, 30 days of samples compress per policy, and the KPI strip is pixel-accurate against the reference.

**🏁 Phase 4 —** 20,000 flows/sec sustain without lag growth, enrichment resolves correctly across a DHCP handover, and the Bandwidth and Top Applications cards match the reference.

**🏁 Phase 5 —** 5,000 syslog events/sec sustain, all seven vendors parse to the normalized schema, an unparseable message is stored not dropped, and a silenced source raises an alert.

**🏁 Phase 6 —** A core-switch failure produces one incident with downstream devices listed, not N alerts. A flapping metric produces one alert. A High alert emails within 30 seconds. A maintenance window suppresses notification but not recording.

**🏁 Phase 7 —** Every supported vendor's config retrieves and diffs, a command outside the allowlist is rejected before dispatch, each built-in baseline evaluates correctly against pass and fail fixtures, and a `.nessus` import correlates to assets with unmatched findings queued.

**🏁 Phase 8 (v1.0) —** Side-by-side against `docs/design/reference-dashboard.png` shows no deviation. A clean host reaches a working login from `docker compose up` and a documented `.env`. A restore from backup reproduces a full working system including time-series data. No container runs as root. SSO maps groups to roles, and a broken SSO config still permits local administrator login.

## Where this will actually hurt

Named now so they are not a surprise later.

1. **Vendor variance.** Every vendor's SNMP, LLDP, and CLI output differs in ways no documentation admits. Phase 1, 5, and 7 estimates are optimistic if you have not recorded fixtures first. Build the `snmpsim` corpus before WP-1.5, not during it.
2. **Time-accurate asset resolution (WP-1.8).** The single highest-leverage package in the build and the easiest to get subtly wrong. Every flow, every log line, every alert is only as trustworthy as this. Review it like a `[SENSITIVE]` package even though it isn't marked one.
3. **Ingest back-pressure (WP-4.1, WP-5.1).** Silent drops under load are the classic failure of every homegrown collector. The disk-spilling channel and its depth metrics are not optional polish.
4. **Alert quality (Phase 6).** A platform that cries wolf gets ignored, and then it is worse than nothing. Hysteresis, dedup, and topology suppression are the difference between a tool you use and a tool you mute.
5. **Timescale policy tuning (WP-3.1).** Compression and retention settings that are wrong at 30 days are painful to change at 300 days. Load-test the schema with synthetic data before Phase 4 puts real volume on it.

## After v1.0

Order these by what the running system actually shows you, not by this list. The Defer column in `SPEC.md` §3 is the candidate pool; the strongest cases are the correlation graph, the config-parsing engine that unlocks both path analysis and firewall rule hygiene, and read-only cloud connectors. Automation and playbooks come last — earn the right to write to production by first running read-only for a year without a false alarm that mattered.
