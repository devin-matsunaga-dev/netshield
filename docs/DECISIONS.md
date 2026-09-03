# DECISIONS.md — NetShield

> Append-only. One line per decision: what was chosen, over what, why, and which package settled it. A later session treats every line here as already settled and does not relitigate it.

## Format

`chose X over Y because Z (WP-N.N, YYYY-MM-DD)`

## Pre-build decisions

These were settled while writing the steering documents, before WP-0.1.

- Chose a modular monolith over microservices because a single administrator deploying to one on-premises host needs one process tree to reason about, and module isolation is enforceable by architecture test without a network boundary (pre-build, 2026-09-03)
- Chose PostgreSQL with TimescaleDB over a separate time-series database because one store means one backup, one connection pool, and one query language across inventory joins and telemetry, which matters more at 500 devices than the throughput a dedicated TSDB would add (pre-build, 2026-09-03)
- Chose to split push ingest into .NET and pull collection into Python over a single collector runtime because syslog and flow reception is socket and back-pressure work while SNMP and network CLI is protocol-library work, and each ecosystem is clearly stronger at one of them (pre-build, 2026-09-03)
- Chose read-only device access with no write path of any kind for V1 over including config push because blast radius on production network kit is the one mistake that cannot be rolled back from a browser (pre-build, 2026-09-03)
- Chose relational adjacency tables over a graph database because V1 needs one-hop topology and downstream-reachability suppression, not multi-hop traversal, and a graph store is a Phase-2 architecture change if correlation is ever built (pre-build, 2026-09-03)
- Chose to defer all automation, threat intelligence, correlation-graph, and path-analysis work over including a thin version of each because a thin version of a correlation engine is worse than none and would set the architecture before the data model is proven (pre-build, 2026-09-03)
- Chose file-import-only vulnerability data over active scanning or NVD lookups because the platform must run with no outbound internet dependency inside a customer network (pre-build, 2026-09-03)
- Chose self-hosted Inter woff2 over a font CDN for the same reason (pre-build, 2026-09-03)
- Chose the reference dashboard screenshot as the binding visual source of truth over an independently authored design system because the design direction is already settled and re-deriving it invites drift (pre-build, 2026-09-03)

## Build decisions

- Chose the classic `.sln` format over the .NET 10 default `.slnx` because `CONVENTIONS.md` §1 names `NetShield.sln` in the binding repository layout (WP-0.1, 2026-09-03)
- Chose FluentAssertions 7.2.0 over 8.x because v7 is the last Apache-2.0 release and v8 onwards requires a paid licence for commercial use, which this project has not confirmed (WP-0.1, 2026-09-03)
- Chose to keep the `Aspire.AppHost.Sdk` 13.5.3 version literal in `NetShield.AppHost.csproj` over moving it into `Directory.Packages.props` because it is an MSBuild SDK reference rather than a `PackageReference`, and central package management does not govern SDK imports (WP-0.1, 2026-09-03)
- Chose `xunit.v3` over the SDK template's xUnit v2 because the test projects were still empty and the migration cost only ever grows (WP-0.1, 2026-09-03)
- Chose to set `TargetFramework` in `Directory.Build.props` and delete it from every `.csproj` over leaving the template's per-project declarations because `ARCHITECTURE.md` §10 admits one version floor and a project cannot drift from a value it does not state (WP-0.1, 2026-09-03)
- Chose `insert_final_newline = true` over the `dotnet new editorconfig` default of `false` because the repository will also hold TypeScript and Python, where a trailing newline is the universal convention (WP-0.1, 2026-09-03)
- Chose to enforce this package's own "Done when" criteria as tests in `NetShield.ArchitectureTests` over accepting the zero-tests allowance in `WORK_PACKAGES.md` because `CONVENTIONS.md` §7 requires tests and the invariants have to hold for every project added after this one, not just the nineteen created here (WP-0.1, 2026-09-03)
- Chose to check those invariants by parsing the `.csproj` files on disk over inspecting loaded assemblies because MSBuild-level rules are not observable from compiled metadata and the file-based check covers projects nothing references yet (WP-0.1, 2026-09-03)
- Chose to delete the Worker and Hello World placeholders the templates generate over keeping them because WP-0.1 produces an empty skeleton and a stub that logs on a timer is invented behaviour a later package would have to unpick (WP-0.1, 2026-09-03)
