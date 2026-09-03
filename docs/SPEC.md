# SPEC.md — NetShield V1 scope

> This document defines the boundary of V1. The **Defer** column is a hard wall, not a backlog hint. If a work package appears to need something from Defer, the session stops and asks the human. Nothing in Defer gets built "just a small version of" during V1.

## 1. What NetShield is

A single-plane network and security operations platform for a small-to-mid enterprise network administrator who wears both the NOC and SOC hat. It discovers what is on the network, watches it, records what it says, correlates events against inventory, and tells one person what needs attention — without them opening five consoles.

**Primary user:** one network & security administrator, possibly with one or two colleagues. Not a 40-analyst SOC.

**Target scale for V1:** 500 monitored devices, 5,000 tracked clients, 20,000 flow records/sec peak, 5,000 syslog events/sec peak, 90 days hot retention. Design for this. Do not build for 100× it.

## 2. Build in V1

| Area | In scope |
|---|---|
| **Inventory** | Device CRUD, credential profiles (vault-backed), scheduled + on-demand discovery via ICMP sweep, SNMP walk, ARP/MAC table and LLDP/CDP neighbor read. Device fingerprinting: vendor, model, OS version, serial, uptime. Manual asset attributes: owner, site, criticality tier, environment, notes. |
| **Topology** | L2/L3 adjacency graph built from LLDP/CDP + ARP + routing tables. Rendered topology map with online/warning/offline node state. VLAN inventory with device and client counts. |
| **Clients** | Endpoint tracking from ARP/MAC/DHCP-lease/wireless-association data. Client identity record: MAC, current + historical IP, hostname, OUI vendor, connected switch port or AP, VLAN, first seen, last seen. **Time-accurate IP-to-asset resolution** — every event resolves to the asset that held the IP at that event's timestamp. |
| **Telemetry** | SNMP polling of interface counters, CPU, memory, temperature, PSU state, optics DOM. ICMP reachability + RTT. Time-series storage with per-metric retention and downsampling. Device health rollup. |
| **Flows** | NetFlow v9 and IPFIX collector. Flow records enriched with asset, client, VLAN, and application identity. Bandwidth utilization by interface, top talkers, top applications, top conversations. |
| **Logs** | Syslog receiver (RFC 3164 + 5424, UDP/TCP/TLS). Vendor parsers normalizing to a common event schema. Full-text + field search over the hot window. Per-source ingest health and silent-source detection. |
| **Alerting** | Rule engine: threshold, rate, absence, and state-change rules over metrics, flows, logs, and inventory state. Deduplication into incidents. Severity model (High / Medium / Low / Info). Topology-aware suppression of downstream device-down alerts. Notification channels: email, webhook. Acknowledge / assign / resolve lifecycle with an audit trail. |
| **Config management** | Scheduled and on-demand config backup over SSH. Version history with diffs. Drift detection against a per-role golden template. Change events raised into the alert stream. |
| **Compliance** | Rule-based config assessment against built-in baselines (CIS-style hardening checks for the supported vendors) plus custom rules in a readable DSL. Per-device and per-baseline pass/fail with evidence. Scheduled compliance reports. |
| **Vulnerabilities** | Import of scanner output (Nessus `.nessus`, OpenVAS XML, CSV). Correlation of findings to inventory assets. Prioritization score combining CVSS, asset criticality, and exposure (internet-facing flag). Remediation status tracking. |
| **Policies** | User-editable alert rules, retention policies, notification routing, discovery schedules, maintenance windows. |
| **Reports** | Scheduled and on-demand PDF/CSV export for inventory, availability, bandwidth, compliance, vulnerability, and alert-activity reports. |
| **Administration** | Local accounts + OIDC SSO, TOTP MFA, role-based access control (Administrator / Operator / Analyst / Read-only), append-only audit log, system health page, license/version info, backup & restore of platform config. |
| **Dashboard** | The reference dashboard in `docs/design/reference-dashboard.png`, with a widget catalog, per-user layout persistence, and a global time-range selector. |

## 3. Defer — do NOT build in V1

| Deferred | Why |
|---|---|
| Automation, playbooks, SOAR, any write action to a network device other than config backup reads | Blast radius. Earning the right to write to production kit takes a mature platform. |
| Any SNMP `set` operation | Same. Read-only SNMP, permanently, in V1. |
| Behavioral analytics / UEBA / peer-group baselining | Needs a year of data and a correlation graph that does not exist yet. |
| Threat intelligence feeds, STIX/TAXII, MISP, IOC matching | Whole subsystem with its own lifecycle and aging model. |
| Graph correlation engine, multi-hop traversal, blast-radius calculation | The V1 store is relational + time-series. A graph model is a Phase-2 architecture change. |
| Path / reachability analysis, what-if simulation | Requires a config-parsing engine per vendor. |
| Firewall rule hygiene (shadowed / redundant / unused rule analysis) | Same engine as above. |
| Packet capture, PCAP storage, DPI beyond flow-level application ID | Storage and legal surface area. |
| Cloud connectors (CloudTrail, VPC/NSG flow logs, Kubernetes audit) | V1 is on-premises network estate only. |
| EDR / IdP / CASB / email-security integrations | Same. |
| Active vulnerability scanning (NetShield performing the scan itself) | V1 imports results; it does not scan. |
| Multi-tenancy, MSP tenant isolation, per-tenant billing | Single-tenant only. |
| Wireless RF analytics, spectrum, client roaming failure analysis | Beyond AP up/down and client association. |
| Synthetic probes, IPSLA/TWAMP, RUM, MOS scoring | Own collector class. |
| ML/anomaly detection of any kind | Static and adaptive thresholds only. |
| High availability, clustering, DR failover | Single-node deployment. Documented backup/restore is the V1 answer. |
| Mobile app, native clients | Responsive web only. |
| Chat integrations (Slack/Teams) beyond generic webhook | Webhook covers it. |
| gNMI / streaming telemetry, NETCONF, RESTCONF | SNMP + SSH only in V1. |
| sFlow, NetFlow v5 | v9 and IPFIX only. |

## 4. Supported vendors in V1

Discovery, config backup, and compliance rules target: **Cisco IOS / IOS-XE**, **Cisco NX-OS**, **Juniper JunOS**, **Arista EOS**, **Fortinet FortiOS**, **MikroTik RouterOS**, and generic **SNMP-only** devices (no CLI features). Anything else falls back to generic SNMP with a clearly-labeled reduced feature set in the UI. Do not add a vendor without a work package that says to.

## 5. Non-functional requirements

- Dashboard first contentful paint under 1.5 s on a warm cache; any dashboard widget query under 500 ms p95 at target scale.
- Flow and syslog ingest must buffer to disk and apply back-pressure rather than drop, and must expose per-source lag as a metric.
- No credential is ever written to a log, an error message, an API response, or the database in plaintext.
- Every state-changing API call is recorded in the append-only audit log with actor, source IP, target, before/after where applicable.
- Every list endpoint is paginated. No endpoint returns an unbounded collection.
- The application runs entirely inside the customer network with no outbound internet dependency at runtime.

## 6. Explicit anti-goals

NetShield is not a SIEM competitor, not a ticketing system, and not a device configuration manager. It reads, correlates, and tells you. Anything that writes to the network waits for V2.
