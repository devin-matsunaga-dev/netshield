# Topology walk fixtures

The neighbour and route walks, recorded in the shape `tests/fixtures/snmp/README.md` defines —
a flat mapping of dotted numeric OID to the string `collector.snmp.session.decode` would have
produced, replayed through `FixtureSession`. Same format, same replayer, different tables.

## These are synthetic

**Nothing here was captured from a device**, exactly as with the fingerprint and client walks.
Every file is hand-authored from the documented shape of its table — LLDP-MIB (IEEE 802.1AB),
CISCO-CDP-MIB, IP-FORWARD-MIB (RFC 4292 and RFC 2096) and RFC 1213's `ipRouteTable` — and every
chassis identifier, host name and address in them is invented from the documentation ranges.
`WORKFLOW.md` § Test data describes the lab that would produce real recordings; it is not stood
up, and WP-2.1 was deliberately not blocked on it at the human's instruction.

The format is the contract, not the content. A recorder writing real `snmpsim` captures of the
same platforms writes the same JSON, and replacing a file here should change nothing but realism.
Committing a capture from a production network is forbidden (CONVENTIONS.md §9).

## The three-switch topology

`core_sw_1`, `acc_sw_1` and `acc_sw_2` are one estate, and between them they are what the WP-2.1
criterion *"a three-switch fixture topology produces the correct edge set"* is measured against.

```
                     core-sw-1  (Arista EOS, LLDP only)
                     00:1C:73:00:00:01 · 192.0.2.11
                        Et1          Et2
                         │            │
              ┌──────────┘            └──────────┐
            Gi0/1                              Gi0/1
     acc-sw-1 (Cisco IOS)                acc-sw-2 (Cisco IOS)
     00:1C:73:00:00:02 · .12            00:1C:73:00:00:03 · .13
     LLDP + CDP                         LLDP + CDP
```

Two links, reported from both ends, so the reconciler has a mirror to find for each and both
edges land at `Confirmed`. The Cisco pair also report the core over CDP, so each of those two
edges carries `Lldp` and `Cdp` together and proves that two protocols agreeing merge into one
edge rather than into two.

**Each switch resolves its LLDP local ports by a different route**, because
`lldpLocPortNum` is not an `ifIndex` and the three ways of discovering that it is not are the
part of this walk most likely to break against a real agent:

| Fixture | `lldpLocPortNum` | How it resolves |
|---|---|---|
| `core_sw_1` | 1, 2, 3 — happens to equal `ifIndex` | by `lldpLocPortId` naming `Et1`, matched to `ifName` |
| `acc_sw_1` | 101, 102 — deliberately unlike `ifIndex` | by `lldpLocPortId` naming `Gi0/1`, matched to `ifName` |
| `acc_sw_2` | 1, 2 — no `lldpLocPortTable` at all | by the port number being an `ifIndex` the device reported |

`acc_sw_1`'s offset numbering is the important one: it is what would fail if the join were ever
quietly replaced by the identity.

## The others

| Fixture | What it is for |
|---|---|
| `conflicting_neighbors` | LLDP and CDP naming *different* far ends on one port. The deterministic-resolution criterion. |
| `unresolvable_ports` | LLDP entries on port numbers the device reported no interface for. They are dropped, not attributed to a guess. |
| `no_neighbors` | Neither protocol answers. Both readings are `supported: false`, which is what stops an absence being read as a withdrawal. |
| `router_routes` | `inetCidrRouteTable` with IPv4 and IPv6 remote routes, local routes that name no gateway, and two routes sharing one next hop. |
| `legacy_routes` | `ipRouteTable` alone — the RFC 1213 fallback, where `indirect(4)` is the word for a remote route. |
| `no_routes` | No routing table at all. `supported: false`. |
| `edge_sw_endpoints` | An access switch whose LLDP neighbours are *endpoints* — an access point, an IP phone, a workstation — beside one uplink. The capability column in all four of its shapes. |

## The capability column is not a number

`lldpRemSysCapEnabled` is `BITS (SIZE (2))` and `cdpCacheCapabilities` is
`OCTET STRING (SIZE (4))`. Both therefore reach a fixture as whatever
`collector.snmp.session.decode` would have written, which for either is colon-separated hex —
a capability word always has a zero octet in it, and a zero byte is not printable. So a
bridge-and-router neighbour is `28:00` and never `28` or `40`.

The two are read by different rules and the difference is deliberate. `BITS` numbers its
members from the *most* significant bit of the first octet, so `28:00` is bits 2 and 4 —
bridge and router. CISCO-CDP-MIB's four octets are one big-endian integer whose bit 0 is worth
1, so `00:00:00:0B` is router, transparent bridge and switch. Reading either with the other's
rule names the wrong capabilities rather than failing, which is why
`collector.snmp.octets` has a function per shape and neither is the default.
