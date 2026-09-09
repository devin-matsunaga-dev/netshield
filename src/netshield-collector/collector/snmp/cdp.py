"""CDP: Cisco's own neighbour protocol, read from Cisco's own MIB.

``cdpCacheTable`` is easier than ``lldpRemTable`` in one way and weaker in another.

Easier, because ``cdpCacheIfIndex`` is the first sub-identifier of the index, so the local
interface needs no join at all — none of ``lldpLocPortTable``'s three-route resolution.

Weaker, because ``cdpCacheDeviceId`` is a host name on most platforms and a serial number on
some, and WP-1.1 settled that a host name is not an identity: DHCP naming, cloned systems, split
DNS and reused defaults all produce duplicates. So a CDP neighbour is a good hint and a poor
identifier, which is exactly why the reconciler on the API side lets LLDP outrank it where the
two disagree about one port.

Only the two Cisco adapters declare CDP (CONVENTIONS.md §5: the vendor decides and shared code
does not branch on a name). Reading a Cisco enterprise subtree from a Juniper costs a round trip
to learn nothing, and asking would be the sort of guess the vendor seam exists to prevent.
"""

from __future__ import annotations

import ipaddress
from collections.abc import Mapping
from dataclasses import dataclass
from typing import Final

import structlog

from collector.snmp import oids
from collector.snmp.octets import big_endian_int
from collector.snmp.ports import PortNames
from collector.snmp.session import SnmpSession
from collector.snmp.tables import number, rows, text

_LOG: Final = structlog.get_logger(__name__)


@dataclass(frozen=True, slots=True)
class CdpNeighbor:
    """One neighbour a Cisco device has heard over CDP."""

    local_if_index: int
    local_port_name: str | None
    device_id: str
    device_port: str | None = None
    platform: str | None = None
    version: str | None = None
    address: str | None = None
    capabilities: int | None = None


@dataclass(frozen=True, slots=True)
class CdpReading:
    """What one device said over CDP, and whether it said anything at all.

    ``supported`` is false for every device that is not a Cisco, and for a Cisco with CDP
    disabled. The distinction the API needs is the same one LLDP needs: an empty cache and an
    absent MIB look identical from a walk, and only one of them is grounds for withdrawing an
    edge.
    """

    supported: bool
    neighbors: tuple[CdpNeighbor, ...] = ()
    truncated: bool = False


async def read_cdp(
    session: SnmpSession,
    ports: PortNames,
    *,
    max_rows: int,
    max_records: int,
) -> CdpReading:
    """Read the device's CDP cache."""
    walked = await session.walk(oids.CDP_CACHE_TABLE, max_rows=max_rows)

    if not walked:
        return CdpReading(supported=False)

    neighbors = parse_cache(walked, ports)

    _LOG.debug("collector.snmp.cdp-read", entries=len(neighbors))

    truncated = len(neighbors) > max_records

    if truncated:
        _LOG.warning(
            "collector.snmp.cdp-truncated",
            found=len(neighbors),
            reported=max_records,
        )

    return CdpReading(
        supported=True,
        neighbors=neighbors[:max_records],
        truncated=truncated,
    )


def parse_cache(walked: Mapping[str, str], ports: PortNames) -> tuple[CdpNeighbor, ...]:
    """Rows of ``cdpCacheTable``, as neighbours.

    The index is ``cdpCacheIfIndex.cdpCacheDeviceIndex``. An entry on an interface the device did
    not itself report is kept anyway, unlike LLDP's — the index *is* the interface index here,
    with no join to have got it wrong, so there is nothing to distrust about it.
    """
    neighbors: list[CdpNeighbor] = []

    for index, row in rows(walked, oids.CDP_CACHE_TABLE).items():
        parts = index.split(".")

        if len(parts) != 2 or not all(part.isdigit() for part in parts):
            continue

        if_index = int(parts[0])
        device_id = text(row, oids.CDP_CACHE_DEVICE_ID)

        if device_id is None:
            continue

        neighbors.append(
            CdpNeighbor(
                local_if_index=if_index,
                local_port_name=ports.name_for(if_index),
                device_id=device_id,
                device_port=text(row, oids.CDP_CACHE_DEVICE_PORT),
                platform=text(row, oids.CDP_CACHE_PLATFORM),
                version=text(row, oids.CDP_CACHE_VERSION),
                address=parse_address(
                    text(row, oids.CDP_CACHE_ADDRESS),
                    number(row, oids.CDP_CACHE_ADDRESS_TYPE),
                ),
                capabilities=big_endian_int(
                    text(row, oids.CDP_CACHE_CAPABILITIES),
                    max_octets=oids.CDP_CACHE_CAPABILITY_OCTETS,
                ),
            )
        )

    neighbors.sort(key=lambda neighbor: (neighbor.local_if_index, neighbor.device_id))

    return tuple(neighbors)


def parse_address(value: str | None, address_type: int | None) -> str | None:
    """``cdpCacheAddress`` as an address, or nothing.

    The column is a ``NetworkAddress``, which is an octet string of raw address bytes rather than
    text — so ``collector.snmp.session.decode`` renders it as colon-separated hex in the ordinary
    case, where at least one byte is not printable. It renders it as *text* in the case where all
    four happen to be, which is why the length is checked against the family before the
    characters are read as bytes: an IPv4 address is four octets and a dotted quad is never four
    characters, so the two forms cannot be confused for one another.

    Anything else is dropped rather than guessed at. The neighbour is still recorded — it just
    loses the one field that could have matched it to a device NetShield already has.
    """
    if value is None:
        return None

    length = 4 if address_type == oids.CDP_ADDRESS_TYPE_IP else 16

    if address_type not in (oids.CDP_ADDRESS_TYPE_IP, oids.CDP_ADDRESS_TYPE_IPV6):
        return None

    octets = _hex_octets(value)

    if octets is None and len(value) == length:
        octets = [ord(character) for character in value]

    if octets is None or len(octets) != length or any(octet > 255 for octet in octets):
        return None

    return str(ipaddress.ip_address(bytes(octets)))


def _hex_octets(value: str) -> list[int] | None:
    """``C0:00:02:0A`` as its bytes, or nothing when the value is not that shape."""
    parts = value.split(":")

    if len(parts) < 2:
        return None

    try:
        return [int(part, 16) for part in parts]
    except ValueError:
        return None
