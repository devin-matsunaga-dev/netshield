"""The neighbour cache: which hardware address holds which IP, as the device sees it.

Two tables answer the same question and neither is universal. ``ipNetToPhysicalTable`` (IP-MIB,
RFC 4293) covers IPv4 and IPv6 in one walk and is what a modern agent implements;
``ipNetToMediaTable`` (RFC 1213) is IPv4-only and deprecated, and is still the only one plenty of
agents answer. So the modern table is read first and the older one only when it produced nothing,
which keeps a device that implements both from being walked twice.

Nothing here is vendor-specific. An ARP cache is an ARP cache, which is why client tracking works
on a device NetShield cannot otherwise identify.
"""

from __future__ import annotations

import ipaddress
from collections.abc import Mapping
from dataclasses import dataclass
from typing import Final

import structlog

from collector.snmp import oids
from collector.snmp.session import SnmpSession
from collector.snmp.tables import number, rows, text

_LOG: Final = structlog.get_logger(__name__)


@dataclass(frozen=True, slots=True)
class NeighborRecord:
    """One entry of a device's neighbour cache."""

    ip_address: str
    mac_address: str
    if_index: int | None = None


@dataclass(frozen=True, slots=True)
class NeighborReading:
    """What one device said about its neighbour cache, and whether it said anything at all.

    ``supported`` is the distinction the API cannot do without: an access switch implements no
    neighbour table, and reporting that as an empty cache would say the estate has no endpoints
    when the device was never asked a question it could answer.
    """

    supported: bool
    records: tuple[NeighborRecord, ...] = ()
    truncated: bool = False


async def read_neighbors(
    session: SnmpSession,
    *,
    max_rows: int,
    max_records: int,
) -> NeighborReading:
    """Read the device's neighbour cache, preferring the modern table."""
    physical = parse_physical(await session.walk(oids.IP_NET_TO_PHYSICAL_TABLE, max_rows=max_rows))

    if physical:
        _LOG.debug("collector.snmp.arp-read", table="ipNetToPhysical", entries=len(physical))

        return _bounded(physical, max_records)

    media = parse_media(await session.walk(oids.IP_NET_TO_MEDIA_TABLE, max_rows=max_rows))

    if media:
        _LOG.debug("collector.snmp.arp-read", table="ipNetToMedia", entries=len(media))

        return _bounded(media, max_records)

    # Neither table produced a usable row. That is genuinely ambiguous — a router with an empty
    # cache and a switch with no cache at all look identical from here — and the honest thing is
    # to report it as unsupported: "nothing to say" is weaker evidence than "nothing there", and
    # the API must not close an interval on the weaker one.
    return NeighborReading(supported=False)


def parse_physical(walked: Mapping[str, str]) -> tuple[NeighborRecord, ...]:
    """Rows of ``ipNetToPhysicalTable``, as records.

    The address is in the index rather than in a column, which is the whole difficulty of this
    table: the index is ``ifIndex.addressType.addressLength.address…``, so the address has to be
    reassembled from the sub-identifiers that follow the length. Doing it from the index is not a
    shortcut — RFC 4293 gives the entry no address column at all.
    """
    records: list[NeighborRecord] = []

    for index, row in rows(walked, oids.IP_NET_TO_PHYSICAL_TABLE).items():
        if not _usable_physical(row):
            continue

        parsed = _parse_physical_index(index)

        if parsed is None:
            continue

        if_index, address = parsed
        mac = text(row, oids.IP_NET_TO_PHYSICAL_PHYS_ADDRESS)

        if mac is None:
            continue

        records.append(NeighborRecord(ip_address=address, mac_address=mac, if_index=if_index))

    return tuple(records)


def parse_media(walked: Mapping[str, str]) -> tuple[NeighborRecord, ...]:
    """Rows of ``ipNetToMediaTable``, as records.

    Simpler than the modern table: the address is a column of its own, so the index is only read
    for the interface. An entry whose address column is missing is dropped rather than
    reconstructed from the index, because the two would have to agree and there is no reason to
    trust the index over what the agent actually answered.
    """
    records: list[NeighborRecord] = []

    for index, row in rows(walked, oids.IP_NET_TO_MEDIA_TABLE).items():
        if number(row, oids.IP_NET_TO_MEDIA_TYPE) == oids.IP_NET_TO_MEDIA_TYPE_INVALID:
            continue

        address = text(row, oids.IP_NET_TO_MEDIA_NET_ADDRESS)
        mac = text(row, oids.IP_NET_TO_MEDIA_PHYS_ADDRESS)

        if address is None or mac is None or not _is_address(address):
            continue

        parts = index.split(".")
        if_index = int(parts[0]) if parts and parts[0].isdigit() else None

        records.append(NeighborRecord(ip_address=address, mac_address=mac, if_index=if_index))

    return tuple(records)


def _usable_physical(row: Mapping[str, str]) -> bool:
    """Whether this entry binds an address to a hardware address at all.

    ``invalid`` is a row on its way out, and ``incomplete`` is the device saying it asked and got
    no answer. Neither names an endpoint, and recording one would attribute an address to a MAC
    the device itself does not claim to have found.
    """
    if number(row, oids.IP_NET_TO_PHYSICAL_TYPE) == oids.IP_NET_TO_PHYSICAL_TYPE_INVALID:
        return False

    return number(row, oids.IP_NET_TO_PHYSICAL_STATE) != oids.IP_NET_TO_PHYSICAL_STATE_INCOMPLETE


def _parse_physical_index(index: str) -> tuple[int | None, str] | None:
    """``ifIndex.addressType.addressLength.address…`` as an interface and an address.

    The length is trusted over the number of sub-identifiers left: an agent that appended
    anything after the address would otherwise produce a longer byte string that is not an
    address, and the length is what RFC 4293 says the address is.
    """
    parts = index.split(".")

    if len(parts) < 4 or not all(part.isdigit() for part in parts):
        return None

    numbers = [int(part) for part in parts]
    if_index, address_type, length = numbers[0], numbers[1], numbers[2]
    octets = numbers[3 : 3 + length]

    if len(octets) != length or any(octet > 255 for octet in octets):
        return None

    if address_type == oids.ADDRESS_TYPE_IPV4 and length == 4:
        return if_index, ".".join(str(octet) for octet in octets)

    if address_type == oids.ADDRESS_TYPE_IPV6 and length == 16:
        return if_index, str(ipaddress.IPv6Address(bytes(octets)))

    # A zone-suffixed IPv6 address, or an address family NetShield does not track. Dropped
    # rather than guessed at: an address assembled from the wrong number of octets would be a
    # confident attribution to a host that does not exist.
    return None


def _is_address(value: str) -> bool:
    try:
        ipaddress.ip_address(value)
    except ValueError:
        return False

    return True


def _bounded(records: tuple[NeighborRecord, ...], max_records: int) -> NeighborReading:
    """The reading, cut to what one result may carry.

    Truncation is reported rather than hidden, because it changes what an absence means: a
    reading that hit its ceiling saw part of the cache, so nothing missing from it is missing
    from the device.
    """
    if len(records) <= max_records:
        return NeighborReading(supported=True, records=records)

    _LOG.warning(
        "collector.snmp.arp-truncated",
        found=len(records),
        reported=max_records,
    )

    return NeighborReading(supported=True, records=records[:max_records], truncated=True)
