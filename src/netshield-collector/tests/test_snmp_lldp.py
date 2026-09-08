"""LLDP: the local-port join, the remote table, and what an absence is allowed to mean."""

from __future__ import annotations

import pytest

from collector.snmp.lldp import (
    parse_local_ports,
    parse_management_addresses,
    parse_remote,
    read_lldp,
    resolve_local_ports,
)
from collector.snmp.ports import PortNames, build
from collector.snmp.session import FixtureSession
from tests.conftest import topology_walk_fixture, topology_walk_session

LOC_PORT_SUBTYPE = "1.0.8802.1.1.2.1.3.7.1.2"
LOC_PORT_ID = "1.0.8802.1.1.2.1.3.7.1.3"
REM = "1.0.8802.1.1.2.1.4.1.1"
REM_MAN = "1.0.8802.1.1.2.1.4.2.1.4"
IF_DESCR = "1.3.6.1.2.1.2.2.1.2"


def ports(**named: int) -> PortNames:
    """An interface map from ``name=index`` pairs."""
    return build({f"{IF_DESCR}.{index}": name for name, index in named.items()}, {})


def remote_row(
    *,
    time_mark: int = 1,
    port_num: int = 1,
    rem_index: int = 1,
    chassis: str = "00:1C:73:00:00:01",
    chassis_subtype: str = "4",
    port_id: str = "Et1",
    port_subtype: str = "5",
) -> dict[str, str]:
    key = f"{time_mark}.{port_num}.{rem_index}"

    return {
        f"{REM}.4.{key}": chassis_subtype,
        f"{REM}.5.{key}": chassis,
        f"{REM}.6.{key}": port_subtype,
        f"{REM}.7.{key}": port_id,
    }


# --- the local-port join ---------------------------------------------------------------------


def test_resolve_local_ports_matches_an_interface_name_to_its_index() -> None:
    local = parse_local_ports({f"{LOC_PORT_SUBTYPE}.101": "5", f"{LOC_PORT_ID}.101": "Gi0/1"})

    assert resolve_local_ports(local, ports(**{"Gi0/1": 1})) == {101: 1}


def test_resolve_local_ports_reads_a_local_port_id_that_is_an_interface_index() -> None:
    local = parse_local_ports({f"{LOC_PORT_SUBTYPE}.9": "7", f"{LOC_PORT_ID}.9": "3"})

    assert resolve_local_ports(local, ports(**{"Gi0/3": 3})) == {9: 3}


def test_resolve_local_ports_falls_back_to_the_port_number_as_an_index() -> None:
    local = parse_local_ports({f"{LOC_PORT_SUBTYPE}.2": "7", f"{LOC_PORT_ID}.2": "some-label"})

    assert resolve_local_ports(local, ports(**{"Gi0/2": 2})) == {2: 2}


def test_resolve_local_ports_never_invents_an_index_the_device_did_not_report() -> None:
    local = parse_local_ports({f"{LOC_PORT_SUBTYPE}.44": "7", f"{LOC_PORT_ID}.44": "port-44"})

    assert resolve_local_ports(local, ports(**{"Gi0/1": 1})) == {}


def test_resolve_local_ports_prefers_the_name_over_the_port_number() -> None:
    """The port number is a convention and the name is evidence, so the name has to win.

    Port 1 advertises the name of interface 5. A resolver that tried the port number first would
    attribute every neighbour on it to interface 1, which is a different cable.
    """
    local = parse_local_ports({f"{LOC_PORT_SUBTYPE}.1": "5", f"{LOC_PORT_ID}.1": "Et5"})

    assert resolve_local_ports(local, ports(Et1=1, Et5=5)) == {1: 5}


# --- the remote table ------------------------------------------------------------------------


def test_parse_remote_places_a_neighbour_on_its_resolved_interface() -> None:
    local = parse_local_ports({f"{LOC_PORT_SUBTYPE}.101": "5", f"{LOC_PORT_ID}.101": "Gi0/1"})
    neighbors = parse_remote(remote_row(port_num=101), local, ports(**{"Gi0/1": 1}), {})

    assert len(neighbors) == 1
    assert neighbors[0].local_if_index == 1
    assert neighbors[0].local_port_name == "Gi0/1"
    assert neighbors[0].chassis_id == "00:1C:73:00:00:01"


def test_parse_remote_names_the_identifier_kinds_rather_than_their_numbers() -> None:
    """``4`` is a MAC address on a chassis id and a network address on a port id.

    One integer travelling to the API would have to be interpreted twice against two enumerations
    that share no numbering, so the collector resolves each against its own table.
    """
    neighbors = parse_remote(
        remote_row(chassis_subtype="4", port_subtype="4"),
        {},
        ports(Et1=1),
        {},
    )

    assert neighbors[0].chassis_id_kind == "MacAddress"
    assert neighbors[0].port_id_kind == "NetworkAddress"


def test_parse_remote_reports_an_unrecognised_subtype_as_unknown() -> None:
    neighbors = parse_remote(remote_row(chassis_subtype="99"), {}, ports(Et1=1), {})

    assert neighbors[0].chassis_id_kind == "Unknown"


def test_parse_remote_drops_a_neighbour_with_no_chassis_identifier() -> None:
    """Everything downstream is built on the chassis id, so an entry without one names nothing."""
    row = remote_row()
    del row[f"{REM}.5.1.1.1"]

    assert parse_remote(row, {}, ports(Et1=1), {}) == ()


def test_parse_remote_drops_an_entry_on_a_port_that_resolves_to_no_interface() -> None:
    assert parse_remote(remote_row(port_num=44), {}, ports(Et1=1), {}) == ()


def test_parse_remote_ignores_the_time_mark_so_a_refreshed_entry_is_the_same_neighbour() -> None:
    """``lldpRemTimeMark`` is a ``TimeFilter`` and moves whenever the entry is refreshed."""
    first = parse_remote(remote_row(time_mark=10), {}, ports(Et1=1), {})
    second = parse_remote(remote_row(time_mark=99_999), {}, ports(Et1=1), {})

    assert first == second


def test_parse_remote_drops_an_index_that_is_not_three_sub_identifiers() -> None:
    assert parse_remote({f"{REM}.5.1.1": "00:11:22:33:44:55"}, {}, ports(Et1=1), {}) == ()


# --- management addresses --------------------------------------------------------------------


def test_parse_management_addresses_reassembles_an_ipv4_address_from_the_index() -> None:
    addresses = parse_management_addresses({f"{REM_MAN}.1.1.1.1.4.192.0.2.11": "1"})

    assert addresses == {(1, 1): "192.0.2.11"}


def test_parse_management_addresses_reassembles_an_ipv6_address_from_the_index() -> None:
    octets = ".".join(str(byte) for byte in bytes.fromhex("20010db8000000000000000000000001"))
    addresses = parse_management_addresses({f"{REM_MAN}.1.2.3.2.16.{octets}": "1"})

    assert addresses == {(2, 3): "2001:db8::1"}


def test_parse_management_addresses_keeps_the_first_of_several_in_index_order() -> None:
    addresses = parse_management_addresses(
        {
            f"{REM_MAN}.1.1.1.1.4.192.0.2.99": "1",
            f"{REM_MAN}.1.1.1.1.4.192.0.2.11": "1",
        }
    )

    assert addresses == {(1, 1): "192.0.2.11"}


def test_parse_management_addresses_drops_a_length_the_octets_do_not_match() -> None:
    assert parse_management_addresses({f"{REM_MAN}.1.1.1.1.4.192.0.2": "1"}) == {}


def test_parse_management_addresses_drops_an_address_family_netshield_does_not_track() -> None:
    assert parse_management_addresses({f"{REM_MAN}.1.1.1.16.6.1.2.3.4.5.6": "1"}) == {}


# --- the reading -----------------------------------------------------------------------------


@pytest.mark.asyncio
async def test_read_lldp_reads_the_core_switch_seeing_both_access_switches() -> None:
    reading = await read_lldp(
        topology_walk_session("core_sw_1"),
        await _ports("core_sw_1"),
        max_rows=5_000,
        max_records=100,
    )

    assert reading.supported
    assert reading.local_chassis_id == "00:1C:73:00:00:01"
    assert reading.local_chassis_id_kind == "MacAddress"
    assert reading.local_system_name == "core-sw-1"
    assert [(n.local_if_index, n.system_name) for n in reading.neighbors] == [
        (1, "acc-sw-1"),
        (2, "acc-sw-2"),
    ]
    assert [n.management_address for n in reading.neighbors] == ["192.0.2.12", "192.0.2.13"]


@pytest.mark.asyncio
async def test_read_lldp_joins_a_port_number_that_is_nothing_like_the_interface_index() -> None:
    """``acc_sw_1`` numbers its LLDP ports 101 and 102 against interfaces 1 and 2."""
    reading = await read_lldp(
        topology_walk_session("acc_sw_1"),
        await _ports("acc_sw_1"),
        max_rows=5_000,
        max_records=100,
    )

    assert [n.local_if_index for n in reading.neighbors] == [1]
    assert reading.neighbors[0].local_port_name == "Gi0/1"


@pytest.mark.asyncio
async def test_read_lldp_falls_back_to_the_port_number_when_there_is_no_local_port_table() -> None:
    reading = await read_lldp(
        topology_walk_session("acc_sw_2"),
        await _ports("acc_sw_2"),
        max_rows=5_000,
        max_records=100,
    )

    assert [n.local_if_index for n in reading.neighbors] == [1]
    assert reading.neighbors[0].chassis_id == "00:1C:73:00:00:01"


@pytest.mark.asyncio
async def test_read_lldp_drops_the_neighbour_on_an_unresolvable_port_and_keeps_the_rest() -> None:
    reading = await read_lldp(
        topology_walk_session("unresolvable_ports"),
        await _ports("unresolvable_ports"),
        max_rows=5_000,
        max_records=100,
    )

    assert [n.chassis_id for n in reading.neighbors] == ["00:1C:73:00:00:01"]


@pytest.mark.asyncio
async def test_read_lldp_reports_unsupported_rather_than_empty_when_nothing_answers() -> None:
    """An absence is not evidence, and the API withdraws an edge on the strength of one."""
    reading = await read_lldp(
        topology_walk_session("no_neighbors"),
        await _ports("no_neighbors"),
        max_rows=5_000,
        max_records=100,
    )

    assert not reading.supported
    assert reading.neighbors == ()


@pytest.mark.asyncio
async def test_read_lldp_says_so_when_it_hit_its_ceiling() -> None:
    values = topology_walk_fixture("core_sw_1")
    reading = await read_lldp(
        FixtureSession(values),
        await _ports("core_sw_1"),
        max_rows=5_000,
        max_records=1,
    )

    assert reading.truncated
    assert len(reading.neighbors) == 1


@pytest.mark.asyncio
async def test_read_lldp_does_not_claim_truncation_when_it_read_everything() -> None:
    reading = await read_lldp(
        topology_walk_session("core_sw_1"),
        await _ports("core_sw_1"),
        max_rows=5_000,
        max_records=2,
    )

    assert not reading.truncated


async def _ports(name: str) -> PortNames:
    from collector.snmp.ports import read_port_names

    return await read_port_names(topology_walk_session(name), max_rows=5_000)
