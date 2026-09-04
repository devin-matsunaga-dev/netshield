"""The interface inventory: joining ifTable to ifXTable, and the speed columns' disagreement."""

from __future__ import annotations

from collector.snmp import oids
from collector.snmp.interfaces import InterfaceRecord, interfaces
from collector.snmp.tables import rows
from tests.conftest import walk_fixture


def parse(fixture: str) -> dict[int, InterfaceRecord]:
    values = walk_fixture(fixture)
    found = interfaces(
        {oid: value for oid, value in values.items() if oid.startswith(f"{oids.IF_TABLE}.")},
        {oid: value for oid, value in values.items() if oid.startswith(f"{oids.IF_X_TABLE}.")},
    )

    return {record.index: record for record in found}


def test_the_two_tables_are_joined_on_ifindex() -> None:
    first = parse("cisco_ios")[1]

    assert first.description == "GigabitEthernet0/1"
    assert first.name == "Gi0/1"
    assert first.alias == "uplink to core"
    assert first.interface_type == 6
    assert first.mtu == 1500
    assert first.admin_status == 1
    assert first.oper_status == 1


def test_a_physical_address_survives_as_hex_rather_than_as_text() -> None:
    """ifDescr and ifPhysAddress are the same SNMP type; only one of them is text."""
    assert parse("cisco_ios")[1].physical_address == "00:1A:2B:3C:4D:01"


def test_ifhighspeed_is_preferred_because_ifspeed_saturates() -> None:
    """A 10G port reports the 32-bit ceiling in ifSpeed; ifXTable is the RFC 2863 answer."""
    ten_gig = parse("cisco_ios")[2]

    assert ten_gig.speed_bits_per_second == 10_000_000_000


def test_ifspeed_is_used_where_the_device_implements_no_ifxtable() -> None:
    ether1 = parse("mikrotik_routeros")[1]

    assert ether1.speed_bits_per_second == 1_000_000_000
    assert ether1.name is None
    assert ether1.alias is None


def test_a_saturated_ifspeed_with_no_ifhighspeed_beside_it_is_no_speed_at_all() -> None:
    """4294967295 is the gauge giving up, not a measurement of 4.29 Gbit/s."""
    sfp = parse("mikrotik_routeros")[2]

    assert sfp.speed_bits_per_second is None


def test_an_empty_alias_reads_as_absent_rather_than_as_a_blank() -> None:
    assert parse("cisco_ios")[2].alias is None


def test_a_row_whose_index_is_not_an_integer_is_dropped() -> None:
    """ifIndex is defined as an integer, and two tables cannot be joined on anything else."""
    found = interfaces(
        {
            f"{oids.IF_INDEX}.1": "1",
            f"{oids.IF_DESCR}.1": "eth0",
            f"{oids.IF_DESCR}.1.2": "a sub-identifier that is not an ifIndex",
        },
        {},
    )

    assert [record.index for record in found] == [1]


def test_records_come_back_in_ifindex_order_whatever_order_they_were_walked_in() -> None:
    found = interfaces(
        {f"{oids.IF_DESCR}.{index}": f"eth{index}" for index in (10, 2, 1)},
        {},
    )

    assert [record.index for record in found] == [1, 2, 10]


def test_a_device_with_no_interfaces_yields_none_rather_than_failing() -> None:
    assert interfaces({}, {}) == ()


def test_columns_are_grouped_by_the_index_that_follows_them() -> None:
    grouped = rows(walk_fixture("cisco_ios"), oids.IF_TABLE)

    assert set(grouped) == {"1", "2"}
    assert grouped["1"][oids.IF_DESCR] == "GigabitEthernet0/1"
