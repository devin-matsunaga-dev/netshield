"""SNMP: walking a device, resolving what it is, and inventorying its interfaces.

One module per protocol (CONVENTIONS.md §5). Everything vendor-specific lives in
``collector/vendors/`` behind the ``VendorAdapter`` protocol and is reached through the registry,
so there is no vendor ``if`` chain anywhere in here.

Read only. This package sends ``get`` and ``getbulk`` and nothing else; the SNMP write operation
appears nowhere in NetShield and is forbidden by ARCHITECTURE.md §1 rather than merely
unimplemented.

``SnmpWalkExecutor`` is one of two walks a ``Discover`` job can name — the other is
``collector.discovery``'s range sweep — and it is reached through ``DiscoverExecutor`` rather
than being registered for the kind itself. It does not import anything from that package: the
dispatcher's contract is structural, so the two protocols stay independent and ``__main__`` is
the only place that knows both exist.
"""

from collector.snmp.executor import WALK_NAME, SnmpJobError, SnmpWalkExecutor
from collector.snmp.session import PySnmpSession, SnmpError, SnmpSession

__all__ = [
    "WALK_NAME",
    "PySnmpSession",
    "SnmpError",
    "SnmpJobError",
    "SnmpSession",
    "SnmpWalkExecutor",
]
