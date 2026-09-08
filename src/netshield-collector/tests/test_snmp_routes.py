"""The routing table, and its reduction to the gateways that are actually adjacencies."""

from __future__ import annotations

import pytest

from collector.snmp.ports import build, read_port_names
from collector.snmp.routes import (
    RouteHop,
    parse_inet_cidr,
    parse_ip_cidr,
    parse_ip_route,
    read_routes,
    reduce_hops,
)
from collector.snmp.session import FixtureSession
from tests.conftest import topology_walk_session

INET = "1.3.6.1.2.1.4.24.7.1"
CIDR = "1.3.6.1.2.1.4.24.4.1"
LEGACY = "1.3.6.1.2.1.4.21.1"
IF_DESCR = "1.3.6.1.2.1.2.2.1.2"

EMPTY_PORTS = build({}, {})

# Every routing table spells "this route names no gateway" as the unspecified address, and that
# is what these tests are about. `ruff`'s S104 reads the literal as a socket binding to every
# interface, which is the same shape of false positive as its S105 on a lease token — so it is
# silenced once, here, where the value is a route's next hop and nothing opens a socket at all.
UNSPECIFIED = "0.0.0.0"  # noqa: S104


def inet_row(
    *,
    destination: str = "10.1.0.0",
    prefix: int = 16,
    next_hop: str = "192.0.2.11",
    if_index: int = 1,
    route_type: int = 4,
) -> dict[str, str]:
    index = ".".join(
        [
            "1",
            "4",
            *destination.split("."),
            str(prefix),
            "2",
            "0",
            "0",
            "1",
            "4",
            *next_hop.split("."),
        ]
    )

    return {f"{INET}.7.{index}": str(if_index), f"{INET}.8.{index}": str(route_type)}


# --- inetCidrRouteTable ----------------------------------------------------------------------


def test_parse_inet_cidr_reads_the_next_hop_out_of_the_length_prefixed_index() -> None:
    hops, routes = parse_inet_cidr(inet_row())

    assert routes == 1
    assert hops == (RouteHop(address="192.0.2.11", if_index=1),)


def test_parse_inet_cidr_counts_every_route_and_keeps_only_the_remote_ones() -> None:
    walked = {
        **inet_row(destination="10.1.0.0"),
        **inet_row(destination="192.0.2.0", prefix=24, next_hop=UNSPECIFIED, route_type=3),
    }

    hops, routes = parse_inet_cidr(walked)

    assert routes == 2
    assert len(hops) == 1


def test_parse_inet_cidr_drops_a_route_whose_next_hop_is_unspecified() -> None:
    """A ``0.0.0.0`` gateway on a row claiming to be remote is a connected route wearing a hat."""
    hops, _ = parse_inet_cidr(inet_row(next_hop=UNSPECIFIED))

    assert hops == ()


def test_parse_inet_cidr_drops_a_loopback_gateway() -> None:
    hops, _ = parse_inet_cidr(inet_row(next_hop="127.0.0.1"))

    assert hops == ()


def test_parse_inet_cidr_reads_an_ipv6_next_hop() -> None:
    destination = ".".join(str(byte) for byte in bytes.fromhex("20010db8000000000000000000000000"))
    hop = ".".join(str(byte) for byte in bytes.fromhex("fe800000000000000000000000000001"))
    index = f"2.16.{destination}.32.2.0.0.2.16.{hop}"

    hops, _ = parse_inet_cidr({f"{INET}.7.{index}": "1", f"{INET}.8.{index}": "4"})

    assert hops == (RouteHop(address="fe80::1", if_index=1),)


def test_parse_inet_cidr_drops_an_index_whose_lengths_do_not_add_up() -> None:
    index = "1.4.10.1.0.0.16.2.0.0.1.4.192.0"

    hops, _ = parse_inet_cidr({f"{INET}.7.{index}": "1", f"{INET}.8.{index}": "4"})

    assert hops == ()


def test_parse_inet_cidr_drops_an_index_that_is_not_all_numbers() -> None:
    index = "1.4.10.1.0.0.16.2.0.0.1.four.192.0.2.11"

    hops, _ = parse_inet_cidr({f"{INET}.7.{index}": "1", f"{INET}.8.{index}": "4"})

    assert hops == ()


# --- ipCidrRouteTable and ipRouteTable -------------------------------------------------------


def test_parse_ip_cidr_reads_the_next_hop_from_its_own_column() -> None:
    index = "10.4.0.0.255.255.0.0.0.192.0.2.11"
    walked = {
        f"{CIDR}.4.{index}": "192.0.2.11",
        f"{CIDR}.5.{index}": "3",
        f"{CIDR}.6.{index}": "4",
    }

    hops, routes = parse_ip_cidr(walked)

    assert routes == 1
    assert hops == (RouteHop(address="192.0.2.11", if_index=3),)


def test_parse_ip_route_treats_indirect_as_the_remote_route() -> None:
    walked = {
        f"{LEGACY}.2.10.9.0.0": "1",
        f"{LEGACY}.7.10.9.0.0": "192.0.2.11",
        f"{LEGACY}.8.10.9.0.0": "4",
    }

    hops, _ = parse_ip_route(walked)

    assert hops == (RouteHop(address="192.0.2.11", if_index=1),)


def test_parse_ip_route_ignores_a_direct_route() -> None:
    walked = {
        f"{LEGACY}.7.192.0.2.0": UNSPECIFIED,
        f"{LEGACY}.8.192.0.2.0": "3",
    }

    assert parse_ip_route(walked)[0] == ()


# --- the reduction ---------------------------------------------------------------------------


def test_reduce_hops_counts_how_many_routes_named_each_gateway() -> None:
    records = reduce_hops(
        [
            RouteHop("192.0.2.11", 1),
            RouteHop("192.0.2.11", 1),
            RouteHop("192.0.2.12", 2),
        ]
    )

    assert [(r.address, r.route_count) for r in records] == [("192.0.2.11", 2), ("192.0.2.12", 1)]


def test_reduce_hops_keeps_one_gateway_reached_over_two_interfaces_apart() -> None:
    """Two interfaces to one gateway is two cables, and each is its own adjacency."""
    records = reduce_hops([RouteHop("192.0.2.11", 1), RouteHop("192.0.2.11", 2)])

    assert len(records) == 2


def test_reduce_hops_leads_with_the_gateway_the_table_leans_on_most() -> None:
    records = reduce_hops(
        [RouteHop("192.0.2.99", 9), RouteHop("192.0.2.11", 1), RouteHop("192.0.2.11", 1)]
    )

    assert records[0].address == "192.0.2.11"


# --- the reading -----------------------------------------------------------------------------


@pytest.mark.asyncio
async def test_read_routes_reads_a_router_and_names_the_table_it_used() -> None:
    session = topology_walk_session("router_routes")
    ports = await read_port_names(topology_walk_session("router_routes"), max_rows=5_000)

    reading = await read_routes(session, ports, max_rows=20_000, max_records=100)

    assert reading.supported
    assert reading.table == "inetCidrRoute"
    assert reading.route_count == 5
    assert [(r.address, r.route_count, r.local_port_name) for r in reading.next_hops] == [
        ("192.0.2.11", 2, "Et1"),
        ("192.0.2.12", 1, "Et2"),
        ("fe80::1", 1, "Et1"),
    ]


@pytest.mark.asyncio
async def test_read_routes_falls_back_to_the_oldest_table() -> None:
    session = topology_walk_session("legacy_routes")
    ports = await read_port_names(topology_walk_session("legacy_routes"), max_rows=5_000)

    reading = await read_routes(session, ports, max_rows=20_000, max_records=100)

    assert reading.table == "ipRoute"
    assert [r.address for r in reading.next_hops] == ["192.0.2.11"]


@pytest.mark.asyncio
async def test_read_routes_reports_unsupported_when_no_table_answers() -> None:
    session = topology_walk_session("no_routes")

    reading = await read_routes(session, EMPTY_PORTS, max_rows=20_000, max_records=100)

    assert not reading.supported
    assert reading.next_hops == ()
    assert reading.table is None


@pytest.mark.asyncio
async def test_read_routes_calls_a_table_of_only_connected_routes_supported_and_empty() -> None:
    """The device answered. It simply forwards through nobody, which is a fact about the device."""
    walked = inet_row(next_hop=UNSPECIFIED, route_type=3)

    reading = await read_routes(
        FixtureSession(walked), EMPTY_PORTS, max_rows=20_000, max_records=100
    )

    assert reading.supported
    assert reading.route_count == 1
    assert reading.next_hops == ()


@pytest.mark.asyncio
async def test_read_routes_does_not_walk_the_older_table_once_the_modern_one_answered() -> None:
    walked = {
        **inet_row(next_hop="192.0.2.11"),
        f"{LEGACY}.7.10.9.0.0": "192.0.2.99",
        f"{LEGACY}.8.10.9.0.0": "4",
    }

    reading = await read_routes(
        FixtureSession(walked), EMPTY_PORTS, max_rows=20_000, max_records=100
    )

    assert [r.address for r in reading.next_hops] == ["192.0.2.11"]


@pytest.mark.asyncio
async def test_read_routes_says_so_when_it_hit_its_ceiling() -> None:
    walked = {
        **inet_row(destination="10.1.0.0", next_hop="192.0.2.11"),
        **inet_row(destination="10.2.0.0", next_hop="192.0.2.12"),
    }

    reading = await read_routes(FixtureSession(walked), EMPTY_PORTS, max_rows=20_000, max_records=1)

    assert reading.truncated
    assert len(reading.next_hops) == 1
