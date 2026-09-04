# `collector/vendors`

One module per vendor, each declaring a `VendorAdapter` (see `base.py`). SPEC.md §4 fixes the
list: Cisco IOS / IOS-XE, Cisco NX-OS, Juniper JunOS, Arista EOS, Fortinet FortiOS, MikroTik
RouterOS, and generic SNMP as the fallback with a reduced, clearly-labelled feature set.

All seven are here. WP-1.3 defined the protocol and the registry and shipped no adapters; WP-1.5
added the SNMP members the protocol had deliberately left out and filled the list in. Phase 7
adds the SSH members for configuration retrieval, to the same protocol and the same modules.

## What an adapter is for

Two things, and nothing else:

1. **Recognising a device** — from its `sysObjectID` arc, or failing that from a `sysDescr`
   marker. `VendorRegistry.resolve` asks every adapter and takes the best answer, so recognition
   is a loop rather than a chain.
2. **Reading the three facts out of the right place** — model, OS version, serial. ENTITY-MIB
   answers for most platforms and is the shared default in `SnmpVendorAdapter`; a vendor that
   puts them somewhere better overrides `scalar_oids` to name its private OIDs and `describe` to
   read them.

An adapter never sees a socket, a credential, or a pysnmp value. It is handed a `SystemGroup`, a
mapping of the scalars it asked for, and the parsed `entPhysicalTable`.

## Two rules, whatever is added

- **Read only.** No SNMP `set`, no configuration mode, no command outside a per-vendor read-only
  allowlist. This is architectural, not stylistic (ARCHITECTURE.md §1, SPEC.md §3), and
  `tests/test_snmp_session.py` fails if any spelling of a write appears in code under
  `collector/`.
- **No vendor `if` chains in shared code.** A vendor difference belongs in that vendor's module,
  reached through `VendorRegistry` (CONVENTIONS.md §5).

## Adding one

Do not, without a work package that says to — SPEC.md §4 is explicit. When one does:

1. A module here with an adapter subclassing `SnmpVendorAdapter`, declaring its arc and markers.
2. A `DeviceVendor` member on the API side, spelled identically. The two are matched by string.
3. A recorded walk under `tests/fixtures/snmp/`, and a row in the fingerprint test's table.
