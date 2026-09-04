"""SNMP: walking a device, resolving what it is, and inventorying its interfaces.

One module per protocol (CONVENTIONS.md §5). Everything vendor-specific lives in
``collector/vendors/`` behind the ``VendorAdapter`` protocol and is reached through the registry,
so there is no vendor ``if`` chain anywhere in here.

Read only. This package sends ``get`` and ``getbulk`` and nothing else; the SNMP write operation
appears nowhere in NetShield and is forbidden by ARCHITECTURE.md §1 rather than merely
unimplemented.

``SnmpWalkExecutor`` is deliberately not re-exported from the modules beneath it, for the reason
``probe`` is not re-exported from ``collector.icmp``: a package attribute shadowing one of its
own submodules makes the dotted name mean two different things depending on how it was imported.
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
