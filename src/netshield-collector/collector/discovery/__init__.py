"""Discovery: sweeping a range of addresses to find hosts nobody has entered.

One module per protocol (CONVENTIONS.md §5), and this one is where the ``Discover`` job kind is
resolved into the two walks it can be. :class:`DiscoverExecutor` is the executor
``ExecutorRegistry`` holds for the kind; the SNMP fingerprint walk in ``collector.snmp`` and the
range sweep beside it are the walks it dispatches to.

Read only, like everything else here. A sweep sends echo requests and nothing else — it asks
whether anything is at an address and carries no instruction, which is the only kind of traffic
NetShield ever sends (ARCHITECTURE.md §1).

The ``sweep`` function stays reachable as ``collector.discovery.sweep.sweep`` and is deliberately
not lifted to this level, for the reason ``probe`` is not lifted out of ``collector.icmp``: a
package attribute that shadows one of its own submodules makes the dotted name mean two different
things depending on how it was imported.
"""

from collector.discovery.executor import (
    SWEEP_NAME,
    DiscoverExecutor,
    DiscoveryJobError,
    DiscoveryWalk,
    RangeSweepExecutor,
    RangeSweepJobParameters,
)

__all__ = [
    "SWEEP_NAME",
    "DiscoverExecutor",
    "DiscoveryJobError",
    "DiscoveryWalk",
    "RangeSweepExecutor",
    "RangeSweepJobParameters",
]
