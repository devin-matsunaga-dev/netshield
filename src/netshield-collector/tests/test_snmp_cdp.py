"""CDP: the cache, and the address column that is raw octets rather than text."""

from __future__ import annotations

import pytest

from collector.snmp.cdp import parse_address, parse_cache, read_cdp
from collector.snmp.ports import PortNames, build, read_port_names
from tests.conftest import topology_walk_fixture, topology_walk_session

CDP = "1.3.6.1.4.1.9.9.23.1.2.1.1"
IF_DESCR = "1.3.6.1.2.1.2.2.1.2"


def ports(**named: int) -> PortNames:
    return build({f"{IF_DESCR}.{index}": name for name, index in named.items()}, {})


def entry(
    *,
    if_index: int = 1,
    device_index: int = 1,
    columns: dict[str, str] | None = None,
) -> dict[str, str]:
    """One cdpCacheTable row, keyed by column number so a test can name the odd ones."""
    key = f"{if_index}.{device_index}"
    row = {f"{CDP}.6.{key}": "core-sw-1", f"{CDP}.7.{key}": "Ethernet1"}
    row.update({f"{CDP}.{column}.{key}": value for column, value in (columns or {}).items()})

    return row


# --- the cache -------------------------------------------------------------------------------


def test_parse_cache_takes_the_local_interface_straight_from_the_index() -> None:
    """Unlike LLDP there is no join here: ``cdpCacheIfIndex`` is the first sub-identifier."""
    neighbors = parse_cache(entry(if_index=7), ports(**{"Gi0/7": 7}))

    assert neighbors[0].local_if_index == 7
    assert neighbors[0].local_port_name == "Gi0/7"


def test_parse_cache_keeps_an_entry_on_an_interface_the_device_did_not_otherwise_report() -> None:
    """There is no join to have got wrong, so there is nothing to distrust about the index."""
    neighbors = parse_cache(entry(if_index=99), ports(**{"Gi0/1": 1}))

    assert len(neighbors) == 1
    assert neighbors[0].local_if_index == 99
    assert neighbors[0].local_port_name is None


def test_parse_cache_drops_an_entry_with_no_device_id() -> None:
    row = entry()
    del row[f"{CDP}.6.1.1"]

    assert parse_cache(row, ports()) == ()


def test_parse_cache_drops_an_index_that_is_not_two_sub_identifiers() -> None:
    assert parse_cache({f"{CDP}.6.1": "core-sw-1"}, ports()) == ()


def test_parse_cache_orders_by_interface_then_device_so_a_reading_is_stable() -> None:
    walked = {
        **entry(if_index=2, device_index=1, columns={"6": "zulu"}),
        **entry(if_index=1, device_index=2, columns={"6": "bravo"}),
        **entry(if_index=1, device_index=1, columns={"6": "alpha"}),
    }

    assert [n.device_id for n in parse_cache(walked, ports())] == ["alpha", "bravo", "zulu"]


# --- cdpCacheAddress -------------------------------------------------------------------------


def test_parse_address_reads_the_colon_hex_form_decode_produces_for_raw_octets() -> None:
    assert parse_address("C0:00:02:0B", 1) == "192.0.2.11"


def test_parse_address_reads_four_printable_octets_as_bytes() -> None:
    """``decode`` renders an octet string as text when every byte happens to be printable.

    ``49.50.51.52`` is ``1234``, and an IPv4 address is four octets while a dotted quad is never
    four characters — so the length against the family is what tells the two forms apart.
    """
    assert parse_address("1234", 1) == "49.50.51.52"


def test_parse_address_reads_an_ipv6_neighbour() -> None:
    raw = ":".join(f"{byte:02X}" for byte in bytes.fromhex("20010db8000000000000000000000001"))

    assert parse_address(raw, 20) == "2001:db8::1"


def test_parse_address_drops_an_address_family_netshield_does_not_track() -> None:
    assert parse_address("C0:00:02:0B", 7) is None


def test_parse_address_drops_a_value_of_the_wrong_length_for_its_family() -> None:
    assert parse_address("C0:00:02", 1) is None


def test_parse_address_drops_a_value_that_is_neither_hex_nor_the_right_length() -> None:
    assert parse_address("not-an-address", 1) is None


def test_parse_address_answers_nothing_for_an_absent_column() -> None:
    assert parse_address(None, 1) is None


def test_parse_cache_keeps_a_neighbour_whose_address_could_not_be_read() -> None:
    """Losing the address costs the match to a known device, not the neighbour."""
    neighbors = parse_cache(entry(columns={"3": "1", "4": "nonsense"}), ports())

    assert len(neighbors) == 1
    assert neighbors[0].address is None


# --- the reading -----------------------------------------------------------------------------


@pytest.mark.asyncio
async def test_read_cdp_reads_an_access_switch_seeing_the_core() -> None:
    session = topology_walk_session("acc_sw_1")
    reading = await read_cdp(
        session,
        await read_port_names(topology_walk_session("acc_sw_1"), max_rows=5_000),
        max_rows=5_000,
        max_records=100,
    )

    assert reading.supported
    assert len(reading.neighbors) == 1
    assert reading.neighbors[0].device_id == "core-sw-1"
    assert reading.neighbors[0].device_port == "Ethernet1"
    assert reading.neighbors[0].address == "192.0.2.11"
    assert reading.neighbors[0].platform == "Arista DCS-7050SX3-48YC8"


@pytest.mark.asyncio
async def test_read_cdp_reports_unsupported_on_a_device_with_no_cdp_cache() -> None:
    reading = await read_cdp(
        topology_walk_session("core_sw_1"),
        await read_port_names(topology_walk_session("core_sw_1"), max_rows=5_000),
        max_rows=5_000,
        max_records=100,
    )

    assert not reading.supported
    assert reading.neighbors == ()


@pytest.mark.asyncio
async def test_read_cdp_says_so_when_it_hit_its_ceiling() -> None:
    from collector.snmp.session import FixtureSession

    values = topology_walk_fixture("acc_sw_1")
    values.update(entry(if_index=2, device_index=1, columns={"6": "second-neighbour"}))

    reading = await read_cdp(
        FixtureSession(values),
        await read_port_names(topology_walk_session("acc_sw_1"), max_rows=5_000),
        max_rows=5_000,
        max_records=1,
    )

    assert reading.truncated
    assert len(reading.neighbors) == 1
