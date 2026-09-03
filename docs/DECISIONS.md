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

_Appended by each package as it completes._
