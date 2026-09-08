"""The forwarding-database reader, against recorded walks.

Three things have to be right here and nothing else is interesting. The bridge port has to become
an ``ifIndex``, because a bridge port number means nothing outside the bridge that issued it. The
entries that describe the switch rather than something attached to it have to be dropped. And the
per-port address count has to be taken over the whole table, because it is the only evidence
telling an access port from a trunk and a count taken after truncation would say they are the
same.
"""

from __future__ import annotations

from collector.snmp import oids
from collector.snmp.bridge import parse_base_ports, parse_dot1q, read_forwarding
from collector.snmp.session import FixtureSession
from tests.conftest import client_walk_fixture, client_walk_session


async def test_read_forwarding_resolves_every_port_to_an_interface() -> None:
    reading = await read_forwarding(
        client_walk_session("layer3_switch"), max_rows=5000, max_records=1000
    )

    assert reading.supported
    assert {record.if_index for record in reading.records} == {1, 2, 3, 48}


async def test_read_forwarding_carries_the_vlan_the_address_was_learned_on() -> None:
    reading = await read_forwarding(
        client_walk_session("layer3_switch"), max_rows=5000, max_records=1000
    )

    by_mac = {record.mac_address: record for record in reading.records}

    assert by_mac["AA:BB:CC:00:00:21"].vlan_id == 10
    assert by_mac["AA:BB:CC:00:00:33"].vlan_id == 20


async def test_read_forwarding_counts_the_addresses_on_each_port() -> None:
    # The evidence that separates an access port from a trunk without a topology to ask.
    reading = await read_forwarding(
        client_walk_session("layer3_switch"), max_rows=5000, max_records=1000
    )

    by_mac = {record.mac_address: record for record in reading.records}

    assert by_mac["AA:BB:CC:00:00:21"].mac_count_on_port == 1
    assert by_mac["AA:BB:CC:00:00:31"].mac_count_on_port == 4


async def test_read_forwarding_drops_the_bridges_own_address() -> None:
    # `self` is a fact about the switch, not about something plugged into it. Recording it would
    # put every switch in the estate in the client list.
    reading = await read_forwarding(
        client_walk_session("layer3_switch"), max_rows=5000, max_records=1000
    )

    assert all(record.mac_address != "00:1A:2B:3C:4D:48" for record in reading.records)


async def test_read_forwarding_when_only_the_vlan_unaware_table_answers_falls_back_to_it() -> None:
    reading = await read_forwarding(
        client_walk_session("access_switch_legacy"), max_rows=5000, max_records=1000
    )

    assert reading.supported
    assert {record.if_index for record in reading.records} == {101, 102}
    assert all(record.vlan_id is None for record in reading.records)


async def test_read_forwarding_with_a_port_the_base_table_does_not_name_drops_the_entry() -> None:
    reading = await read_forwarding(
        client_walk_session("access_switch_legacy"), max_rows=5000, max_records=1000
    )

    assert all(record.mac_address != "AA:BB:CC:00:00:44" for record in reading.records)


async def test_read_forwarding_with_an_unlearned_port_drops_the_entry() -> None:
    # Port 0 is a real value in both MIBs and it attributes nothing.
    reading = await read_forwarding(
        client_walk_session("access_switch_legacy"), max_rows=5000, max_records=1000
    )

    assert all(record.mac_address != "AA:BB:CC:00:00:45" for record in reading.records)


async def test_read_forwarding_against_a_router_reports_unsupported_rather_than_empty() -> None:
    reading = await read_forwarding(client_walk_session("router"), max_rows=5000, max_records=1000)

    assert not reading.supported
    assert reading.records == ()


async def test_read_forwarding_above_the_report_ceiling_truncates_and_says_so() -> None:
    reading = await read_forwarding(
        client_walk_session("layer3_switch"), max_rows=5000, max_records=3
    )

    assert reading.supported
    assert reading.truncated
    assert len(reading.records) == 3


async def test_read_forwarding_prefers_the_vlan_aware_table_when_both_answer() -> None:
    both = dict(client_walk_fixture("layer3_switch"))
    both.update(
        {
            "1.3.6.1.2.1.17.4.3.1.2.170.187.204.0.0.99": "1",
            "1.3.6.1.2.1.17.4.3.1.3.170.187.204.0.0.99": "3",
        }
    )

    reading = await read_forwarding(FixtureSession(both), max_rows=5000, max_records=1000)

    assert all(record.mac_address != "AA:BB:CC:00:00:99" for record in reading.records)


def test_parse_base_ports_reads_the_port_to_interface_mapping() -> None:
    assert parse_base_ports(client_walk_fixture("access_switch_legacy")) == {1: 101, 2: 102}


def test_parse_dot1q_renders_the_address_as_colon_separated_uppercase_hex() -> None:
    # What `collector.snmp.session.decode` would have produced for an octet string, and what the
    # API's own normaliser reads.
    walked = {
        f"{oids.DOT1Q_TP_FDB_PORT}.10.0.26.43.60.77.1": "1",
        f"{oids.DOT1Q_TP_FDB_STATUS}.10.0.26.43.60.77.1": "3",
    }

    records = parse_dot1q(walked, {1: 1})

    assert [record.mac_address for record in records] == ["00:1A:2B:3C:4D:01"]


def test_parse_dot1q_with_an_index_of_the_wrong_length_drops_the_row() -> None:
    walked = {
        f"{oids.DOT1Q_TP_FDB_PORT}.10.0.26.43": "1",
        f"{oids.DOT1Q_TP_FDB_STATUS}.10.0.26.43": "3",
    }

    assert parse_dot1q(walked, {1: 1}) == ()
