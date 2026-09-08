# Client-table walk fixtures

Five recorded-shape walks covering the ways a device answers — or does not answer — the two
tables the client walk reads. The same rules apply as to the fingerprint fixtures one directory
up: **everything here is synthetic**, hand-authored from the MIB definitions rather than captured
from a device, and every address, MAC and VLAN in them is invented. Committing a real capture
from a production network is forbidden (CONVENTIONS.md §9); `WORKFLOW.md` § Test data describes
the lab that would produce real recordings.

The format is identical — a flat mapping of dotted numeric OID to the string
`collector.snmp.session.decode` would have produced — so a real `snmpsim` recording of the same
platform can replace a file without changing a test.

## What each one is for

| File | Shape | What it proves |
|---|---|---|
| `layer3_switch.json` | `ipNetToPhysicalTable` + `dot1qTpFdbTable` | The ordinary case: both tables answered, VLANs read, `macCountOnPort` separating three access ports from one uplink carrying four addresses. Also that an `incomplete` neighbour entry, an `invalid` one, and a `self` forwarding entry are all dropped. |
| `router.json` | `ipNetToPhysicalTable` only, IPv4 and IPv6 | That a device with no BRIDGE-MIB reports forwarding **unsupported** rather than empty, and that an IPv6 neighbour is reassembled from the sixteen octets of its index. |
| `access_switch_legacy.json` | `dot1dTpFdbTable` only | The VLAN-unaware fallback, reached only because the VLAN-aware table produced nothing. Also that an entry naming a bridge port `dot1dBasePortTable` does not, and one on port 0, are dropped rather than reported against a number that means nothing outside the bridge. |
| `legacy_arp_only.json` | `ipNetToMediaTable` only | The deprecated IPv4 ARP table as a fallback, reached only because `ipNetToPhysicalTable` produced nothing, with an `invalid` entry dropped. |
| `no_client_tables.json` | neither | Both readings unsupported, which is what makes the API leave every binding alone instead of reading silence as "nobody is attached". |

## The distinction these exist to protect

`supported` is not a nicety. A router implements no forwarding database and an access switch no
neighbour cache, so an empty reading is ambiguous at the device and has to stop being ambiguous
before it reaches the interval tables. `router.json` and `no_client_tables.json` are the two
fixtures that pin it: if either ever came back as a *supported, empty* reading, the API would be
free to conclude that nothing is attached to a device that was never asked a question it could
answer.
