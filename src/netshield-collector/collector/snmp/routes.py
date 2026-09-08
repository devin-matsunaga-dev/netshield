"""The routing table, reduced to the thing topology actually wants: distinct next hops.

A route is not an adjacency. What a routing table says about who a device is *next to* is the set
of gateways it forwards through — every remote route names one, and a next hop is by definition
one hop away. So this reads the table and throws almost all of it away, keeping one record per
``(next hop, interface)`` with a count of how many routes used it.

That reduction is why this is worth doing at all. A distribution switch's table is thousands of
rows and a core router's can be far more, and the number of distinct gateways behind them is
usually single digits. Counting in the collector rather than the API is the same choice WP-1.8
made for ``macCountOnPort``: the count is a property of the whole table, and the API only ever
sees what survived the reduction.

**It is a walk of its own, not part of the neighbour walk.** LLDP and CDP are small, bounded and
predictable; a routing table is none of the three. Putting them in one job would mean a device
that answered LLDP and then timed out reading forty thousand routes failed the whole job and
discarded topology it had already established — WP-1.8's known trade, at a much worse ratio.
Two walks cost a second schedule and buy failure isolation.

Three tables answer the same question, in the order a modern agent implements them:
``inetCidrRouteTable`` (RFC 4292, both address families), ``ipCidrRouteTable`` (RFC 2096,
deprecated, IPv4) and ``ipRouteTable`` (RFC 1213, older still). Each is tried only when the one
before it produced nothing, which keeps a device implementing all three from being walked
three times.
"""

from __future__ import annotations

import ipaddress
from collections.abc import Mapping, Sequence
from dataclasses import dataclass
from typing import Any, Final

import structlog
from pydantic import Field, ValidationError

from collector.models import LeasedJob, WireModel
from collector.snmp import oids
from collector.snmp.ports import PortNames, read_port_names
from collector.snmp.session import PySnmpSession, SnmpSession, SnmpSessionFactory
from collector.snmp.tables import number, rows, text

_LOG: Final = structlog.get_logger(__name__)


@dataclass(frozen=True, slots=True)
class NextHopRecord:
    """One gateway this device forwards through, and how much it leans on it."""

    address: str
    if_index: int | None
    route_count: int
    local_port_name: str | None = None


@dataclass(frozen=True, slots=True)
class RouteReading:
    """What one device said about its routing table, and whether it said anything at all.

    ``supported`` is false for a device that implements no routing table — an access switch — and
    for one whose table holds nothing but connected routes. As everywhere else in this package,
    the API must be able to tell that apart from "this device routes through nobody", because one
    of the two is grounds for withdrawing an L3 edge and the other is not.
    """

    supported: bool
    next_hops: tuple[NextHopRecord, ...] = ()
    truncated: bool = False
    route_count: int = 0
    table: str | None = None


async def read_routes(
    session: SnmpSession,
    ports: PortNames,
    *,
    max_rows: int,
    max_records: int,
) -> RouteReading:
    """Read the device's routing table, preferring the modern one.

    A table that answered *anything* is the one that is read, even when every route in it is
    connected and it therefore names no gateway. That is the distinction ``supported`` exists to
    carry: a router with only connected routes has told NetShield something, and an access switch
    with no routing table has not, and only the first is grounds for withdrawing an L3 edge.
    """
    for table in _TABLES:
        walked = await session.walk(_ROOTS[table], max_rows=max_rows)

        if not walked:
            continue

        reading = _bounded(_PARSERS[table](walked), max_records, table=table, ports=ports)

        _LOG.debug(
            "collector.snmp.routes-read",
            table=table,
            routes=reading.route_count,
            nextHops=len(reading.next_hops),
        )

        return reading

    return RouteReading(supported=False)


@dataclass(frozen=True, slots=True)
class RouteHop:
    """One route's contribution, before the reduction to distinct gateways."""

    address: str
    if_index: int | None


def parse_inet_cidr(walked: Mapping[str, str]) -> tuple[tuple[RouteHop, ...], int]:
    """Rows of ``inetCidrRouteTable``, as the hops they name and how many routes there were.

    The next hop is in the index, and the index is the longest in this package: destination type,
    length-prefixed destination, prefix length, length-prefixed policy OID, next-hop type,
    length-prefixed next hop. Every one of the five lengths is trusted over the number of
    sub-identifiers remaining, for the reason ``ipNetToPhysicalTable``'s is — an address
    assembled from the wrong count is a confident statement about a gateway that does not exist.
    """
    hops: list[RouteHop] = []
    routes = 0

    for index, row in rows(walked, oids.INET_CIDR_ROUTE_TABLE).items():
        routes += 1

        if number(row, oids.INET_CIDR_ROUTE_TYPE) != oids.INET_CIDR_ROUTE_TYPE_REMOTE:
            continue

        address = _parse_inet_cidr_index(index)

        if address is None:
            continue

        hops.append(RouteHop(address=address, if_index=number(row, oids.INET_CIDR_ROUTE_IF_INDEX)))

    return tuple(hops), routes


def parse_ip_cidr(walked: Mapping[str, str]) -> tuple[tuple[RouteHop, ...], int]:
    """Rows of ``ipCidrRouteTable``. The next hop is a column of its own here."""
    hops: list[RouteHop] = []
    routes = 0

    for _, row in rows(walked, oids.IP_CIDR_ROUTE_TABLE).items():
        routes += 1

        if number(row, oids.IP_CIDR_ROUTE_TYPE) != oids.IP_CIDR_ROUTE_TYPE_REMOTE:
            continue

        address = _usable(text(row, oids.IP_CIDR_ROUTE_NEXT_HOP))

        if address is None:
            continue

        hops.append(RouteHop(address=address, if_index=number(row, oids.IP_CIDR_ROUTE_IF_INDEX)))

    return tuple(hops), routes


def parse_ip_route(walked: Mapping[str, str]) -> tuple[tuple[RouteHop, ...], int]:
    """Rows of ``ipRouteTable``. ``indirect(4)`` is this table's word for a remote route."""
    hops: list[RouteHop] = []
    routes = 0

    for _, row in rows(walked, oids.IP_ROUTE_TABLE).items():
        routes += 1

        if number(row, oids.IP_ROUTE_TYPE) != oids.IP_ROUTE_TYPE_INDIRECT:
            continue

        address = _usable(text(row, oids.IP_ROUTE_NEXT_HOP))

        if address is None:
            continue

        hops.append(RouteHop(address=address, if_index=number(row, oids.IP_ROUTE_IF_INDEX)))

    return tuple(hops), routes


_TABLES: Final = ("inetCidrRoute", "ipCidrRoute", "ipRoute")
"""The three tables, in the order a modern agent implements them."""

_ROOTS: Final = {
    "inetCidrRoute": oids.INET_CIDR_ROUTE_TABLE,
    "ipCidrRoute": oids.IP_CIDR_ROUTE_TABLE,
    "ipRoute": oids.IP_ROUTE_TABLE,
}

_PARSERS: Final = {
    "inetCidrRoute": parse_inet_cidr,
    "ipCidrRoute": parse_ip_cidr,
    "ipRoute": parse_ip_route,
}


def reduce_hops(hops: Sequence[RouteHop]) -> tuple[NextHopRecord, ...]:
    """One record per distinct ``(next hop, interface)``, carrying how many routes named it.

    Ordered by how much of the table leans on the gateway, then by address, so that a result read
    by a person leads with the one that matters and a result compared by a test is stable.
    """
    counts: dict[tuple[str, int | None], int] = {}

    for hop in hops:
        key = (hop.address, hop.if_index)
        counts[key] = counts.get(key, 0) + 1

    records = [
        NextHopRecord(address=address, if_index=if_index, route_count=count)
        for (address, if_index), count in counts.items()
    ]

    records.sort(key=lambda record: (-record.route_count, record.address, record.if_index or 0))

    return tuple(records)


def _parse_inet_cidr_index(index: str) -> str | None:
    parts = index.split(".")

    if not all(part.isdigit() for part in parts):
        return None

    numbers = [int(part) for part in parts]
    cursor = 0

    def take(count: int) -> list[int] | None:
        nonlocal cursor

        if cursor + count > len(numbers):
            return None

        taken = numbers[cursor : cursor + count]
        cursor += count

        return taken

    # inetCidrRouteDestType, then the length-prefixed destination.
    if take(1) is None:
        return None

    destination_length = take(1)

    if destination_length is None or take(destination_length[0]) is None:
        return None

    # inetCidrRoutePfxLen, then the length-prefixed policy OID.
    if take(1) is None:
        return None

    policy_length = take(1)

    if policy_length is None or take(policy_length[0]) is None:
        return None

    # inetCidrRouteNextHopType, then the length-prefixed next hop, which is what all of this
    # was for.
    hop_type = take(1)
    hop_length = take(1)

    if hop_type is None or hop_length is None:
        return None

    octets = take(hop_length[0])

    if octets is None or any(octet > 255 for octet in octets):
        return None

    if hop_type[0] == oids.ADDRESS_TYPE_IPV4 and len(octets) == 4:
        return _usable(".".join(str(octet) for octet in octets))

    if hop_type[0] == oids.ADDRESS_TYPE_IPV6 and len(octets) == 16:
        return _usable(str(ipaddress.IPv6Address(bytes(octets))))

    return None


def _usable(value: str | None) -> str | None:
    """The address, unless it names no gateway.

    An unspecified next hop — ``0.0.0.0`` or ``::`` — is how every one of these tables spells a
    connected route in a row that claims to be remote, and a loopback gateway is the device
    talking about itself. Neither is an adjacency, and recording one would put every router in
    the estate next to nothing.
    """
    if value is None:
        return None

    try:
        address = ipaddress.ip_address(value.strip())
    except ValueError:
        return None

    if address.is_unspecified or address.is_loopback or address.is_multicast:
        return None

    return str(address)


def _bounded(
    parsed: tuple[tuple[RouteHop, ...], int],
    max_records: int,
    *,
    table: str,
    ports: PortNames,
) -> RouteReading:
    """The reading, reduced, named and cut to what one result may carry."""
    hops, routes = parsed
    records = reduce_hops(hops)

    truncated = len(records) > max_records

    if truncated:
        _LOG.warning(
            "collector.snmp.routes-truncated",
            found=len(records),
            reported=max_records,
        )

    return RouteReading(
        supported=True,
        next_hops=tuple(
            NextHopRecord(
                address=record.address,
                if_index=record.if_index,
                route_count=record.route_count,
                local_port_name=ports.name_for(record.if_index),
            )
            for record in records[:max_records]
        ),
        truncated=truncated,
        route_count=routes,
        table=table,
    )


WALK_NAME: Final = "routes"
"""The discriminator the API writes into a route walk's parameters."""


class RouteWalkJobError(RuntimeError):
    """This job cannot be run as a route walk, and no table was read."""


class RouteWalkJobParameters(WireModel):
    """What the API asked for. Mirrors ``RouteWalkParameters`` on the other side."""

    walk: str
    timeout_seconds: float = Field(gt=0, le=120)
    retries: int = Field(ge=0, le=10)
    max_repetitions: int = Field(ge=1, le=100)
    max_rows: int = Field(ge=1, le=500_000)
    max_next_hops: int = Field(ge=1, le=10_000)


class RouteWalkExecutor:
    """Reads one device's routing table and reports the gateways behind it."""

    walk = WALK_NAME
    """Which ``Discover`` walk this answers for. ``DiscoverExecutor`` dispatches on it."""

    def __init__(self, session_factory: SnmpSessionFactory | None = None) -> None:
        self._session = session_factory or PySnmpSession

    async def execute(self, job: LeasedJob) -> dict[str, Any]:
        """Read the job's device and return the distinct next hops it forwards through.

        Raising is how a job is failed, and it means nothing was established. A route walk that
        times out therefore costs the L3 half of a device's adjacency and nothing else — the LLDP
        and CDP edges the neighbour walk established are on a different job and are untouched,
        which is the whole reason the two are separate walks.
        """
        if job.device is None:
            raise RouteWalkJobError("A route walk needs a device and this job names none.")

        if job.credential is None:
            raise RouteWalkJobError("A route walk needs a credential and this job carries none.")

        parameters = self._parameters(job)

        async with self._session(
            job.device.ip_address,
            job.credential,
            timeout_seconds=parameters.timeout_seconds,
            retries=parameters.retries,
            max_repetitions=parameters.max_repetitions,
        ) as session:
            ports = await read_port_names(session, max_rows=parameters.max_rows)
            reading = await read_routes(
                session,
                ports,
                max_rows=parameters.max_rows,
                max_records=parameters.max_next_hops,
            )

        _LOG.info(
            "collector.snmp.routes-walked",
            jobId=str(job.job_id),
            deviceId=str(job.device.device_id),
            supported=reading.supported,
            table=reading.table,
            routes=reading.route_count,
            nextHops=len(reading.next_hops),
        )

        return payload(reading)

    @staticmethod
    def _parameters(job: LeasedJob) -> RouteWalkJobParameters:
        if job.parameters is None:
            raise RouteWalkJobError(
                "A Discover job carries no parameters saying which walk to run."
            )

        try:
            parameters = RouteWalkJobParameters.model_validate(job.parameters)
        except ValidationError as error:
            raise RouteWalkJobError(
                f"The job parameters are not a route walk's: {error}"
            ) from error

        if parameters.walk != WALK_NAME:
            raise RouteWalkJobError(
                f"This walk runs the {WALK_NAME} walk and this job names {parameters.walk}."
            )

        return parameters


def payload(reading: RouteReading) -> dict[str, Any]:
    """The result shape the API stores and reads.

    Written out member by member with the names the API's own payload type declares, rather than
    dumped from a model. The two shapes live in two repositories' worth of code with no generator
    between them, and a field that changed name on one side should break a test here rather than
    quietly stop being read there.
    """
    return {
        "walk": WALK_NAME,
        "routesSupported": reading.supported,
        "routeTable": reading.table,
        "routeCount": reading.route_count,
        "nextHopCount": len(reading.next_hops),
        "nextHopsTruncated": reading.truncated,
        "nextHops": [
            {
                "address": record.address,
                "ifIndex": record.if_index,
                "localPortName": record.local_port_name,
                "routeCount": record.route_count,
            }
            for record in reading.next_hops
        ],
    }
