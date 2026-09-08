"""Which ``ifIndex`` a port name belongs to, and what to call an ``ifIndex``.

Every table in this package that is not keyed by an interface index has to be joined back to one,
because ``ifIndex`` is what the rest of NetShield speaks — the interface inventory WP-1.5 records,
the client port bindings WP-1.8 opens, the counters Phase 3 will poll. WP-1.8 did that join for
the forwarding database through ``dot1dBasePortTable``; LLDP needs the same thing and has no such
table, because IEEE 802.1AB identifies a port by *name* and leaves the index to the agent.

So this reads two columns and nothing else: ``ifDescr``, which every agent implements, and
``ifName`` from ``ifXTable``, which is the short form a neighbour protocol actually advertises
(``Gi0/1`` rather than ``GigabitEthernet0/1``). Two column walks are much cheaper than the two
whole tables the fingerprint walk reads, and this walk needs no other column of either.
"""

from __future__ import annotations

from collections.abc import Mapping
from dataclasses import dataclass, field
from typing import Final

from collector.snmp import oids
from collector.snmp.session import SnmpSession


@dataclass(frozen=True, slots=True)
class PortNames:
    """What one device calls each of its interfaces, in both directions.

    ``by_name`` is case-folded, because a neighbour advertises the name its own operator typed
    and an agent answers with the name its own configuration holds, and the two differ in case
    often enough that matching exactly would drop real edges.
    """

    names: Mapping[int, str] = field(default_factory=dict)
    """``ifIndex`` to the best name for it: ``ifName`` where there is one, else ``ifDescr``."""

    by_name: Mapping[str, int] = field(default_factory=dict)
    """Every spelling a device gave for a port, case-folded, to its ``ifIndex``."""

    def index_for(self, name: str | None) -> int | None:
        """The ``ifIndex`` a port name belongs to, or nothing when the device named no such port."""
        if name is None:
            return None

        return self.by_name.get(name.strip().casefold())

    def name_for(self, index: int | None) -> str | None:
        """What the device calls that interface, or nothing when it named no such index."""
        return None if index is None else self.names.get(index)

    def knows(self, index: int | None) -> bool:
        """Whether the device reported an interface at that index at all."""
        return index is not None and index in self.names


_EMPTY: Final = PortNames()


async def read_port_names(session: SnmpSession, *, max_rows: int) -> PortNames:
    """Read ``ifDescr`` and ``ifName`` and build the map both ways.

    A device that answers neither leaves an empty map rather than raising. That is not a failure
    — it costs the local-port join, and a neighbour whose port cannot be resolved to an index is
    dropped by the reader that wanted it, which is the same choice WP-1.8 made for a forwarding
    entry naming a bridge port ``dot1dBasePortTable`` did not.
    """
    descriptions = await session.walk(oids.IF_DESCR, max_rows=max_rows)
    names = await session.walk(oids.IF_NAME, max_rows=max_rows)

    return build(descriptions, names)


def build(descriptions: Mapping[str, str], names: Mapping[str, str]) -> PortNames:
    """The map, from two walked columns."""
    described = _column(descriptions, oids.IF_DESCR)
    named = _column(names, oids.IF_NAME)

    if not described and not named:
        return _EMPTY

    best: dict[int, str] = {}
    by_name: dict[str, int] = {}

    for index in sorted(set(described) | set(named)):
        short = named.get(index)
        long = described.get(index)

        chosen = short or long

        if chosen is not None:
            best[index] = chosen

        for spelling in (short, long):
            if spelling is None:
                continue

            # First spelling wins. Two interfaces sharing a name is an agent contradicting
            # itself, and picking the lower index is at least an answer that does not depend on
            # the order a dictionary happened to iterate in.
            by_name.setdefault(spelling.casefold(), index)

    return PortNames(names=best, by_name=by_name)


def _column(walked: Mapping[str, str], column: str) -> dict[int, str]:
    """One walked column as ``{ifIndex: value}``, dropping anything that is not one."""
    prefix = f"{column}."
    values: dict[int, str] = {}

    for oid, value in walked.items():
        if not oid.startswith(prefix):
            continue

        index = oid[len(prefix) :]
        stripped = value.strip()

        if not index.isdigit() or not stripped:
            continue

        values[int(index)] = stripped

    return values
