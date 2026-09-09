# VLAN walk fixtures

The VLAN walk, recorded in the shape `tests/fixtures/snmp/README.md` defines — a flat mapping of
dotted numeric OID to the string `collector.snmp.session.decode` would have produced, replayed
through `FixtureSession`. Same format, same replayer, different tables: Q-BRIDGE-MIB's
`dot1qVlanStaticTable` and `dot1qVlanCurrentTable` (RFC 4363), joined through BRIDGE-MIB's
`dot1dBasePortTable`.

## These are synthetic

**Nothing here was captured from a device**, exactly as with the fingerprint, client and topology
walks. Every file is hand-authored from the documented shape of its table, and every VLAN name and
port number in it is invented. `WORKFLOW.md` § Test data describes the lab that would produce real
recordings; it is not stood up, and WP-2.2 was deliberately not blocked on it at the human's
instruction — the third package to make that trade, after WP-1.5 and WP-2.1.

The format is the contract, not the content. Committing a capture from a production network is
forbidden (CONVENTIONS.md §9).

## A `PortList` is a bitmap, and the recorded format spells it two ways

RFC 4363 says the membership columns are octet strings in which the first octet carries bridge
ports 1 to 8 and the *most* significant bit of an octet is its lowest numbered port. That is a
bitmap, and `decode` does not know it is: it renders an octet string as text when every byte is
printable and as colon-separated uppercase hex otherwise. So one VLAN's members can be recorded
as `00:30` and the next VLAN's as `` ` `` — a single byte `0x60`, which happens to be a printable
character — and both are correct recordings of the same kind of value.

`collector.snmp.vlans._octets` undoes exactly that: the hex spelling is taken only when at least
one byte it decodes to is one `decode` would itself have hexed, and otherwise the string is read
as the bytes it is made of. `test_snmp_vlans.py` pins the two together by round-tripping real
bitmaps through `decode` rather than by asserting against a constant.

A bitmap whose bytes are all printable *and* which also spells valid hex — five bytes reading
`AB:CD`, say — is genuinely ambiguous, and it is the recorded format that is lossy there rather
than the reader. It is read as hex, which is the likelier reading for a bitmap.

## The fixtures

| Fixture | What it is for |
|---|---|
| `core_sw_1` | The whole static table: five named VLANs on trunks, bridge ports 11–16 against interfaces 1–6 so the `dot1dBasePortTable` join cannot be quietly replaced by the identity, one VLAN with no untagged ports, and one `notReady` row that must not reach the inventory. |
| `acc_sw_1` | Bitmaps that decode as printable text rather than as hex — the case a reader assuming hex would silently report as empty. Also a VLAN whose untagged bitmap names a port its egress bitmap does not. |
| `legacy_current` | `dot1qVlanCurrentTable` alone: the fallback, with no names, indexed by `timeMark.vlanId`, and one VLAN present at two time marks. |
| `unresolvable_ports` | A bitmap naming bridge ports the device's own base-port table does not place. They are counted and dropped, not attributed to a guess. |
| `no_vlans` | Neither table answers. The reading is `supported: false`, which is what stops an absence being read as a withdrawal. |

## The five VLANs

`core_sw_1` carries the five the reference dashboard's tiles name — `Server VLAN` (10),
`Users VLAN` (20), `Voice VLAN` (30), `Guest VLAN` (40) and `IoT VLAN` (50) — so that the walk
these fixtures prove and the estate `VlanInventoryTests` asserts the tile counts against are
talking about the same network.
