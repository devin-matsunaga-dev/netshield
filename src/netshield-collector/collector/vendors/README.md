# `collector/vendors`

One module per vendor, each declaring a `VendorAdapter` (see `base.py`). SPEC.md §4 fixes the
list: Cisco IOS / IOS-XE, Cisco NX-OS, Juniper JunOS, Arista EOS, Fortinet FortiOS, MikroTik
RouterOS, and generic SNMP as the fallback with a reduced, clearly-labelled feature set.

There are no adapters here yet. WP-1.3 builds the collector skeleton and the job contract; the
adapters arrive with the packages that can exercise them — WP-1.5 for SNMP walk discovery and
fingerprinting, Phase 7 for configuration retrieval.

Two rules, whatever is added:

- **Read only.** No SNMP `set`, no configuration mode, no command outside a per-vendor read-only
  allowlist. This is architectural, not stylistic (ARCHITECTURE.md §1, SPEC.md §3).
- **No vendor `if` chains in shared code.** A vendor difference belongs in that vendor's module,
  reached through `VendorRegistry` (CONVENTIONS.md §5).
