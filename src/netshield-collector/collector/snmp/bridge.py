"""The forwarding database: which hardware address a switch has learned on which port.

Two tables again, and the same shape of choice as the neighbour cache. ``dot1qTpFdbTable``
(Q-BRIDGE-MIB) is indexed by ``dot1qFdbId.macAddress``, so one walk covers every VLAN and every
entry says which VLAN it was learned on. ``dot1dTpFdbTable`` (BRIDGE-MIB) is indexed by MAC alone
and knows nothing about VLANs — on some platforms it has to be walked once per VLAN through a
community string suffixed ``@vlan``, which is a per-vendor trick this collector deliberately does
not do. So the VLAN-aware table is read first and the older one only when it produced nothing.

**Both are keyed by a bridge port, not by an interface.** A bridge port is a number local to the
bridge, and the rest of NetShield speaks ``ifIndex`` — the interface inventory, the topology
Phase 2 will build, the counters Phase 3 will poll. ``dot1dBasePortTable`` is the join between
them, and without it a forwarding entry names a port nothing else can identify.
"""

from __future__ import annotations

from collections.abc import Mapping
from dataclasses import dataclass
from typing import Final

import structlog

from collector.snmp import oids
from collector.snmp.session import SnmpSession
from collector.snmp.tables import number, rows

_LOG: Final = structlog.get_logger(__name__)


@dataclass(frozen=True, slots=True)
class ForwardingRecord:
    """One entry of a switch's forwarding database, with its port resolved to an ``ifIndex``."""

    mac_address: str
    if_index: int
    vlan_id: int | None = None
    mac_count_on_port: int | None = None


@dataclass(frozen=True, slots=True)
class ForwardingReading:
    """What one device said about its forwarding database, and whether it said anything.

    ``supported`` carries the same weight it does for the neighbour cache: a router implements no
    forwarding database, and reporting that as an empty one would say nothing is attached to a
    device that was never asked a question it could answer.
    """

    supported: bool
    records: tuple[ForwardingRecord, ...] = ()
    truncated: bool = False


async def read_forwarding(
    session: SnmpSession,
    *,
    max_rows: int,
    max_records: int,
) -> ForwardingReading:
    """Read the device's forwarding database, preferring the VLAN-aware table."""
    ports = parse_base_ports(await session.walk(oids.DOT1D_BASE_PORT_TABLE, max_rows=max_rows))

    qbridge = parse_dot1q(
        await session.walk(oids.DOT1Q_TP_FDB_TABLE, max_rows=max_rows),
        ports,
    )

    if qbridge:
        _LOG.debug("collector.snmp.fdb-read", table="dot1qTpFdb", entries=len(qbridge))

        return _bounded(qbridge, max_records)

    dbridge = parse_dot1d(
        await session.walk(oids.DOT1D_TP_FDB_TABLE, max_rows=max_rows),
        ports,
    )

    if dbridge:
        _LOG.debug("collector.snmp.fdb-read", table="dot1dTpFdb", entries=len(dbridge))

        return _bounded(dbridge, max_records)

    return ForwardingReading(supported=False)


def parse_base_ports(walked: Mapping[str, str]) -> dict[int, int]:
    """``dot1dBasePortIfIndex`` as ``{bridge port: ifIndex}``."""
    mapping: dict[int, int] = {}

    for index, row in rows(walked, oids.DOT1D_BASE_PORT_TABLE).items():
        if_index = number(row, oids.DOT1D_BASE_PORT_IF_INDEX)

        if if_index is None or not index.isdigit():
            continue

        mapping[int(index)] = if_index

    return mapping


def parse_dot1q(
    walked: Mapping[str, str],
    base_ports: Mapping[int, int],
) -> tuple[ForwardingRecord, ...]:
    """Rows of ``dot1qTpFdbTable``, as records.

    The index is ``dot1qFdbId.a.b.c.d.e.f``: a filtering-database id followed by the six octets of
    the address. On most platforms the filtering-database id is the VLAN id, which is what makes
    one walk of this table answer the VLAN question at all; where a platform uses shared
    filtering databases the two differ, and the number reported is then the filtering database's
    rather than a VLAN's. That is the table's own ambiguity and not one this can resolve.
    """
    return _records(walked, oids.DOT1Q_TP_FDB_TABLE, base_ports, vlan_aware=True)


def parse_dot1d(
    walked: Mapping[str, str],
    base_ports: Mapping[int, int],
) -> tuple[ForwardingRecord, ...]:
    """Rows of ``dot1dTpFdbTable``, as records. Indexed by the six address octets alone."""
    return _records(walked, oids.DOT1D_TP_FDB_TABLE, base_ports, vlan_aware=False)


def _records(
    walked: Mapping[str, str],
    table: str,
    base_ports: Mapping[int, int],
    *,
    vlan_aware: bool,
) -> tuple[ForwardingRecord, ...]:
    port_column = oids.DOT1Q_TP_FDB_PORT if vlan_aware else oids.DOT1D_TP_FDB_PORT
    status_column = oids.DOT1Q_TP_FDB_STATUS if vlan_aware else oids.DOT1D_TP_FDB_STATUS

    found: list[tuple[int, ForwardingRecord]] = []

    for index, row in rows(walked, table).items():
        status = number(row, status_column)

        # `invalid` is a row being removed. `self` is the bridge's own address on that port,
        # which is a fact about the switch rather than about something attached to it — recording
        # it would put every switch in the estate in the client list.
        if status in (oids.DOT1Q_TP_FDB_STATUS_INVALID, oids.DOT1Q_TP_FDB_STATUS_SELF):
            continue

        parsed = _parse_index(index, vlan_aware=vlan_aware)

        if parsed is None:
            continue

        vlan_id, mac = parsed
        port = number(row, port_column)

        # Port 0 means the bridge has not learned which port the address is on. It is a real
        # value in both MIBs and it attributes nothing.
        if port is None or port == 0:
            continue

        if_index = base_ports.get(port)

        if if_index is None:
            # The forwarding table names a bridge port dot1dBasePortTable did not. Nothing can
            # turn that into an interface the rest of NetShield knows, so the entry is dropped
            # rather than reported against a port number that means nothing outside this bridge.
            continue

        found.append(
            (if_index, ForwardingRecord(mac_address=mac, if_index=if_index, vlan_id=vlan_id))
        )

    # How many addresses each port learned in this same reading. Counted here rather than by the
    # API because it is a property of the whole table and the API only ever sees the entries that
    # survived the report ceiling — a count taken after truncation would say an access port and
    # a trunk had learned the same number.
    counts: dict[int, int] = {}

    for if_index, _ in found:
        counts[if_index] = counts.get(if_index, 0) + 1

    return tuple(
        ForwardingRecord(
            mac_address=record.mac_address,
            if_index=record.if_index,
            vlan_id=record.vlan_id,
            mac_count_on_port=counts[if_index],
        )
        for if_index, record in found
    )


def _parse_index(index: str, *, vlan_aware: bool) -> tuple[int | None, str] | None:
    """The index as a VLAN and a MAC address, rendered the way the API expects to read one."""
    parts = index.split(".")

    if not all(part.isdigit() for part in parts):
        return None

    numbers = [int(part) for part in parts]

    if vlan_aware:
        if len(numbers) != 7:
            return None

        vlan_id, octets = numbers[0], numbers[1:]
    else:
        if len(numbers) != 6:
            return None

        vlan_id, octets = None, numbers

    if any(octet > 255 for octet in octets):
        return None

    # Colon-separated uppercase hex, which is what `collector.snmp.session.decode` renders a
    # non-printable octet string as and what the API's own normaliser reads. The API normalises
    # again on arrival, so a different spelling would still land correctly — matching here is
    # what keeps a fixture readable by a person.
    return vlan_id, ":".join(f"{octet:02X}" for octet in octets)


def _bounded(records: tuple[ForwardingRecord, ...], max_records: int) -> ForwardingReading:
    """The reading, cut to what one result may carry, saying so when it was cut."""
    if len(records) <= max_records:
        return ForwardingReading(supported=True, records=records)

    _LOG.warning("collector.snmp.fdb-truncated", found=len(records), reported=max_records)

    return ForwardingReading(supported=True, records=records[:max_records], truncated=True)
