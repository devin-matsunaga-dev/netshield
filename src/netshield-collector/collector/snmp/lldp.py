"""LLDP: what a device has heard from whatever is plugged into it.

``lldpRemTable`` is IEEE 802.1AB and every platform SPEC.md §4 names implements it, which is why
it is the primary source for adjacency and why CDP is only ever a second opinion. Each entry
carries a *chassis identifier* — usually the neighbour's base MAC address — and that is an
identity, unlike the host name CDP reports. Where the two disagree about one port, this is the
one that wins.

Three things make the table harder to read than it looks.

**The index is not stable.** It is ``lldpRemTimeMark.lldpRemLocalPortNum.lldpRemIndex``, and the
time mark changes every time the entry is refreshed. Nothing may be keyed by it; only the middle
sub-identifier means anything, and it identifies a port.

**That port number is not an ``ifIndex``.** 802.1AB says only that ``lldpLocPortNum`` is locally
unique and stable across reinitialisations. Cisco IOS uses a dense sequence of its own, Juniper
another, and several vendors do happen to use the interface index. So the join is by *name*,
through ``lldpLocPortTable`` and the interface names :mod:`collector.snmp.ports` reads — with
the port number accepted as an index only when the device itself reported an interface there.

**The management address is in the index too.** ``lldpRemManAddrTable`` has no address column;
the address is the tail of the index, length-prefixed, exactly as ``ipNetToPhysicalTable``'s is.
It is worth the extra walk because it is what turns a neighbour into a device NetShield already
knows.
"""

from __future__ import annotations

import ipaddress
from collections.abc import Mapping
from dataclasses import dataclass
from typing import Final

import structlog

from collector.snmp import oids
from collector.snmp.ports import PortNames
from collector.snmp.session import SnmpSession
from collector.snmp.tables import number, rows, text

_LOG: Final = structlog.get_logger(__name__)


@dataclass(frozen=True, slots=True)
class LldpNeighbor:
    """One neighbour, on one local port."""

    local_if_index: int
    local_port_name: str | None
    chassis_id: str
    chassis_id_kind: str
    port_id: str | None = None
    port_id_kind: str = oids.LLDP_ID_KIND_UNKNOWN
    port_description: str | None = None
    system_name: str | None = None
    system_description: str | None = None
    management_address: str | None = None
    capabilities: int | None = None


@dataclass(frozen=True, slots=True)
class LldpReading:
    """What one device said over LLDP, and whether it said anything at all.

    ``supported`` carries the weight it carries everywhere else in this package: a device with
    LLDP disabled and a device with no neighbours look identical from a walk, and only one of the
    two is evidence that a link has gone. The API withdraws an edge on an absence and must
    therefore be able to tell them apart.
    """

    supported: bool
    neighbors: tuple[LldpNeighbor, ...] = ()
    truncated: bool = False
    local_chassis_id: str | None = None
    local_chassis_id_kind: str = oids.LLDP_ID_KIND_UNKNOWN
    local_system_name: str | None = None


async def read_lldp(
    session: SnmpSession,
    ports: PortNames,
    *,
    max_rows: int,
    max_records: int,
) -> LldpReading:
    """Read the device's LLDP remote table, its own advertised identity, and the port join."""
    local = await session.get(list(oids.LLDP_LOCAL_SCALARS))
    local_ports = parse_local_ports(await session.walk(oids.LLDP_LOC_PORT_TABLE, max_rows=max_rows))

    remote = await session.walk(oids.LLDP_REM_TABLE, max_rows=max_rows)

    if not remote:
        # Nothing under lldpRemTable. That is a device with LLDP off, a device nobody is plugged
        # into, and a device that does not implement the MIB, all at once — and none of the three
        # is a statement that a link has gone. Reported as unsupported for the same reason an
        # empty ARP table is: the weaker reading is the safe one.
        return LldpReading(supported=False)

    addresses = parse_management_addresses(
        await session.walk(oids.LLDP_REM_MAN_ADDR_TABLE, max_rows=max_rows)
    )

    neighbors = parse_remote(remote, local_ports, ports, addresses)

    _LOG.debug("collector.snmp.lldp-read", entries=len(neighbors))

    return _bounded(
        neighbors,
        max_records,
        local_chassis_id=text(local, oids.LLDP_LOC_CHASSIS_ID),
        local_chassis_id_kind=_kind(
            number(local, oids.LLDP_LOC_CHASSIS_ID_SUBTYPE),
            oids.LLDP_CHASSIS_ID_SUBTYPES,
        ),
        local_system_name=text(local, oids.LLDP_LOC_SYS_NAME),
    )


def parse_local_ports(walked: Mapping[str, str]) -> dict[int, tuple[str, str | None]]:
    """``lldpLocPortTable`` as ``{lldpLocPortNum: (port id subtype, port id)}``."""
    parsed: dict[int, tuple[str, str | None]] = {}

    for index, row in rows(walked, oids.LLDP_LOC_PORT_TABLE).items():
        if not index.isdigit():
            continue

        parsed[int(index)] = (
            _kind(number(row, oids.LLDP_LOC_PORT_ID_SUBTYPE), oids.LLDP_PORT_ID_SUBTYPES),
            text(row, oids.LLDP_LOC_PORT_ID),
        )

    return parsed


def resolve_local_ports(
    local_ports: Mapping[int, tuple[str, str | None]],
    ports: PortNames,
) -> dict[int, int]:
    """``{lldpLocPortNum: ifIndex}``, by the three routes that actually occur.

    In order, because the earlier ones are evidence and the last one is a convention:

    1. The local port id is an interface name and the device named an interface with it. This is
       the only route that reads what 802.1AB actually says, and it is what Cisco, Juniper and
       Arista all give.
    2. The local port id is a number the device also reported as an ``ifIndex``. Some agents put
       the index in a ``local(7)`` port id.
    3. The port number *is* an ``ifIndex`` the device reported. Common, and never assumed for an
       index the device did not itself name — otherwise a dense 1..N sequence would silently
       attribute every neighbour to whatever interface happened to have that index.

    A port number none of the three resolves is absent from the map, and the neighbours on it are
    dropped: an edge on an interface the rest of NetShield cannot identify is not an edge it can
    do anything with. The same choice WP-1.8 made for a bridge port ``dot1dBasePortTable`` did
    not name.
    """
    resolved: dict[int, int] = {}

    for port_num, (kind, port_id) in local_ports.items():
        if kind in oids.LLDP_INTERFACE_NAME_SUBTYPES:
            by_name = ports.index_for(port_id)

            if by_name is not None:
                resolved[port_num] = by_name

                continue

        if port_id is not None and port_id.isdigit() and ports.knows(int(port_id)):
            resolved[port_num] = int(port_id)

            continue

        if ports.knows(port_num):
            resolved[port_num] = port_num

    return resolved


def parse_remote(
    walked: Mapping[str, str],
    local_ports: Mapping[int, tuple[str, str | None]],
    ports: PortNames,
    addresses: Mapping[tuple[int, int], str],
) -> tuple[LldpNeighbor, ...]:
    """Rows of ``lldpRemTable``, as neighbours on resolved local interfaces."""
    port_indexes = resolve_local_ports(local_ports, ports)

    # A device that answers lldpRemTable and no lldpLocPortTable still has to be readable, and
    # the only thing left to try is the port number as an interface index the device named.
    neighbors: list[LldpNeighbor] = []

    for index, row in sorted(rows(walked, oids.LLDP_REM_TABLE).items(), key=_index_order):
        parsed = _parse_remote_index(index)

        if parsed is None:
            continue

        port_num, rem_index = parsed
        if_index = port_indexes.get(port_num)

        if if_index is None and ports.knows(port_num):
            if_index = port_num

        if if_index is None:
            continue

        chassis_id = text(row, oids.LLDP_REM_CHASSIS_ID)

        # A neighbour with no chassis identifier names nothing. Everything downstream — the
        # remote key, the merge with CDP, the far-end device match — is built on it, and a row
        # without one would be an edge to an anonymous thing that could never be reconciled.
        if chassis_id is None:
            continue

        neighbors.append(
            LldpNeighbor(
                local_if_index=if_index,
                local_port_name=ports.name_for(if_index),
                chassis_id=chassis_id,
                chassis_id_kind=_kind(
                    number(row, oids.LLDP_REM_CHASSIS_ID_SUBTYPE),
                    oids.LLDP_CHASSIS_ID_SUBTYPES,
                ),
                port_id=text(row, oids.LLDP_REM_PORT_ID),
                port_id_kind=_kind(
                    number(row, oids.LLDP_REM_PORT_ID_SUBTYPE),
                    oids.LLDP_PORT_ID_SUBTYPES,
                ),
                port_description=text(row, oids.LLDP_REM_PORT_DESC),
                system_name=text(row, oids.LLDP_REM_SYS_NAME),
                system_description=text(row, oids.LLDP_REM_SYS_DESC),
                management_address=addresses.get((port_num, rem_index)),
                capabilities=number(row, oids.LLDP_REM_SYS_CAP_ENABLED),
            )
        )

    return tuple(neighbors)


def parse_management_addresses(walked: Mapping[str, str]) -> dict[tuple[int, int], str]:
    """``lldpRemManAddrTable`` as ``{(local port num, rem index): address}``.

    The index is ``timeMark.localPortNum.remIndex.addressSubtype.addressLength.address…``, so the
    address is reassembled from the sub-identifiers that follow the length exactly as
    ``ipNetToPhysicalTable``'s is. A neighbour advertising several addresses keeps the first in
    index order, which is deterministic and is the one an operator sees first in any CLI too.
    """
    found: dict[tuple[int, int], str] = {}

    for oid in sorted(walked, key=_oid_order):
        prefix = f"{oids.LLDP_REM_MAN_ADDR_TABLE}."

        if not oid.startswith(prefix):
            continue

        remainder = oid[len(prefix) :]
        _, separator, index = remainder.partition(".")

        if not separator:
            continue

        parsed = _parse_man_addr_index(index)

        if parsed is None:
            continue

        key, address = parsed
        found.setdefault(key, address)

    return found


def _parse_remote_index(index: str) -> tuple[int, int] | None:
    """``timeMark.localPortNum.remIndex`` as the port number and the entry index.

    The time mark is read and discarded deliberately. It is a ``TimeFilter``, which means it
    changes whenever the entry is refreshed — so an index is not a stable key and the same
    neighbour walked twice legitimately appears under two of them.
    """
    parts = index.split(".")

    if len(parts) != 3 or not all(part.isdigit() for part in parts):
        return None

    return int(parts[1]), int(parts[2])


def _parse_man_addr_index(index: str) -> tuple[tuple[int, int], str] | None:
    parts = index.split(".")

    if len(parts) < 6 or not all(part.isdigit() for part in parts):
        return None

    numbers = [int(part) for part in parts]
    port_num, rem_index = numbers[1], numbers[2]
    address_type, length = numbers[3], numbers[4]
    octets = numbers[5 : 5 + length]

    if len(octets) != length or any(octet > 255 for octet in octets):
        return None

    if address_type == oids.ADDRESS_TYPE_IPV4 and length == 4:
        return (port_num, rem_index), ".".join(str(octet) for octet in octets)

    if address_type == oids.ADDRESS_TYPE_IPV6 and length == 16:
        return (port_num, rem_index), str(ipaddress.IPv6Address(bytes(octets)))

    return None


def _kind(subtype: int | None, table: Mapping[int, str]) -> str:
    """One of the API's own identifier-kind names, or ``Unknown``.

    Reported as a name rather than as the number, because the two enumerations 802.1AB defines
    share no numbering — ``4`` is a MAC address on a chassis id and a network address on a port
    id — and a single integer travelling to the API would have to be interpreted twice.
    """
    if subtype is None:
        return oids.LLDP_ID_KIND_UNKNOWN

    return table.get(subtype, oids.LLDP_ID_KIND_UNKNOWN)


def _index_order(item: tuple[str, Mapping[str, str]]) -> tuple[int, ...]:
    return _oid_order(item[0])


def _oid_order(oid: str) -> tuple[int, ...]:
    """Numeric order over a dotted OID, so a walk is read the way an agent must walk it."""
    return tuple(int(part) if part.isdigit() else -1 for part in oid.split("."))


def _bounded(
    neighbors: tuple[LldpNeighbor, ...],
    max_records: int,
    *,
    local_chassis_id: str | None,
    local_chassis_id_kind: str,
    local_system_name: str | None,
) -> LldpReading:
    """The reading, cut to what one result may carry, saying so when it was cut.

    Truncation matters more here than anywhere before it. The API *withdraws* an edge that a
    successful reading did not contain — which is right, because an LLDP table is the device's
    complete current statement and its agent expires its own entries — and a reading that hit its
    ceiling is not complete. So the flag is what stops a large chassis losing half its topology
    the first time it exceeds the ceiling.
    """
    truncated = len(neighbors) > max_records

    if truncated:
        _LOG.warning(
            "collector.snmp.lldp-truncated",
            found=len(neighbors),
            reported=max_records,
        )

    return LldpReading(
        supported=True,
        neighbors=neighbors[:max_records],
        truncated=truncated,
        local_chassis_id=local_chassis_id,
        local_chassis_id_kind=local_chassis_id_kind,
        local_system_name=local_system_name,
    )
