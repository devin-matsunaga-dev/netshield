"""The neighbour cache reader, against recorded walks.

What these have to establish is narrower than it looks. The parsing is mostly index arithmetic —
``ipNetToPhysicalTable`` puts the address in the index rather than in a column — and the rest is
one distinction the API cannot do without: a table the device does not implement is not an empty
table.
"""

from __future__ import annotations

from collector.snmp import oids
from collector.snmp.arp import parse_media, parse_physical, read_neighbors
from collector.snmp.session import FixtureSession
from tests.conftest import client_walk_fixture, client_walk_session


async def test_read_neighbors_against_a_layer3_switch_reads_every_usable_entry() -> None:
    reading = await read_neighbors(
        client_walk_session("layer3_switch"), max_rows=5000, max_records=1000
    )

    assert reading.supported
    assert not reading.truncated
    assert {record.ip_address for record in reading.records} == {
        "10.10.0.21",
        "10.10.0.22",
        "10.10.0.23",
    }


async def test_read_neighbors_with_an_incomplete_entry_drops_it() -> None:
    # The device asked and nothing answered. Recording it would attribute an address to a MAC
    # the device itself does not claim to have found — and to the all-zero address at that.
    reading = await read_neighbors(
        client_walk_session("layer3_switch"), max_rows=5000, max_records=1000
    )

    assert all(record.ip_address != "10.10.0.99" for record in reading.records)


async def test_read_neighbors_with_an_invalid_entry_drops_it() -> None:
    reading = await read_neighbors(
        client_walk_session("layer3_switch"), max_rows=5000, max_records=1000
    )

    assert all(record.ip_address != "10.10.0.98" for record in reading.records)


async def test_read_neighbors_against_a_router_reads_ipv6_from_the_index() -> None:
    reading = await read_neighbors(client_walk_session("router"), max_rows=5000, max_records=1000)

    assert reading.supported
    assert "2001:db8::50" in {record.ip_address for record in reading.records}


async def test_read_neighbors_carries_the_interface_the_entry_was_on() -> None:
    reading = await read_neighbors(
        client_walk_session("layer3_switch"), max_rows=5000, max_records=1000
    )

    assert {record.if_index for record in reading.records} == {10}


async def test_read_neighbors_when_only_the_deprecated_table_answers_falls_back_to_it() -> None:
    reading = await read_neighbors(
        client_walk_session("legacy_arp_only"), max_rows=5000, max_records=1000
    )

    assert reading.supported
    assert {record.ip_address for record in reading.records} == {"172.16.4.10", "172.16.4.11"}


async def test_read_neighbors_with_neither_table_reports_unsupported_rather_than_empty() -> None:
    # The distinction the whole package rests on. An empty *supported* reading would let the API
    # conclude that nothing holds an address, on a device that was never asked a question it
    # could answer.
    reading = await read_neighbors(
        client_walk_session("no_client_tables"), max_rows=5000, max_records=1000
    )

    assert not reading.supported
    assert reading.records == ()


async def test_read_neighbors_above_the_report_ceiling_truncates_and_says_so() -> None:
    reading = await read_neighbors(
        client_walk_session("layer3_switch"), max_rows=5000, max_records=2
    )

    assert reading.supported
    assert reading.truncated
    assert len(reading.records) == 2


async def test_read_neighbors_prefers_the_modern_table_when_both_answer() -> None:
    # A device implementing both must not be read twice, and the modern table is the one that
    # can carry IPv6.
    both = dict(client_walk_fixture("layer3_switch"))
    both.update(
        {
            "1.3.6.1.2.1.4.22.1.2.10.10.10.0.77": "AA:BB:CC:00:00:77",
            "1.3.6.1.2.1.4.22.1.3.10.10.10.0.77": "10.10.0.77",
            "1.3.6.1.2.1.4.22.1.4.10.10.10.0.77": "3",
        }
    )

    reading = await read_neighbors(FixtureSession(both), max_rows=5000, max_records=1000)

    assert all(record.ip_address != "10.10.0.77" for record in reading.records)


def test_parse_physical_with_an_index_shorter_than_its_declared_length_drops_the_row() -> None:
    # An address assembled from the wrong number of octets is a confident attribution to a host
    # that does not exist, which is worse than dropping the row.
    walked = {
        f"{oids.IP_NET_TO_PHYSICAL_PHYS_ADDRESS}.3.1.4.10.0": "AA:BB:CC:00:00:01",
        f"{oids.IP_NET_TO_PHYSICAL_STATE}.3.1.4.10.0": "1",
    }

    assert parse_physical(walked) == ()


def test_parse_physical_with_an_unknown_address_family_drops_the_row() -> None:
    walked = {
        f"{oids.IP_NET_TO_PHYSICAL_PHYS_ADDRESS}.3.4.4.10.0.0.1": "AA:BB:CC:00:00:01",
        f"{oids.IP_NET_TO_PHYSICAL_STATE}.3.4.4.10.0.0.1": "1",
    }

    assert parse_physical(walked) == ()


def test_parse_media_with_no_address_column_drops_the_row() -> None:
    # The index also carries the address, and the two would have to agree. There is no reason to
    # trust the index over what the agent actually answered.
    walked = {f"{oids.IP_NET_TO_MEDIA_PHYS_ADDRESS}.1.10.0.0.1": "AA:BB:CC:00:00:01"}

    assert parse_media(walked) == ()
