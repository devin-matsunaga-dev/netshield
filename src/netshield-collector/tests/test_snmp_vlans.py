"""The VLAN reader, against recorded walks.

Three things have to be right here. A ``PortList`` bit has to become the ``ifIndex`` of the port
it names, through ``dot1dBasePortTable``, because a bridge port number means nothing outside the
bridge that issued it. The bitmap has to survive both spellings ``collector.snmp.session.decode``
can produce for one, because a bitmap is not text and ``decode`` cannot know that. And a device
that answers neither table has to be distinguishable from one whose tables are empty, because the
API withdraws a VLAN an answered reading no longer contains.
"""

from __future__ import annotations

import pytest

from collector.snmp.session import decode
from collector.snmp.vlans import (
    CURRENT_TABLE,
    STATIC_TABLE,
    parse_current,
    parse_static,
    port_list,
    read_vlans,
)
from tests.conftest import vlan_walk_fixture, vlan_walk_session

# --- the bitmap ------------------------------------------------------------------------------


@pytest.mark.parametrize(
    ("ports", "octets"),
    [
        ([], 0),
        ([1], 1),
        ([8], 1),
        ([1, 8], 1),
        ([2, 3], 1),  # 0x60 — the printable byte '`', which decode records as text
        ([3], 1),  # 0x20 — a space, also printable
        ([1, 2, 3, 4, 5, 6, 7, 8], 1),
        ([11, 12, 13], 2),
        ([1, 2, 9], 2),
        ([1, 48], 6),
        ([2, 4, 11, 12], 2),
        (list(range(1, 25)), 3),
    ],
)
def test_port_list_reads_back_whatever_decode_wrote(ports: list[int], octets: int) -> None:
    # The pin that matters. `decode` renders an octet string as text or as hex depending on its
    # bytes, and a bitmap goes through it either way — so the reader is proved by round-tripping
    # real bitmaps through the real `decode`, not by asserting against a spelling chosen here.
    raw = bytearray(octets)

    for port in ports:
        raw[(port - 1) // 8] |= 0x80 >> ((port - 1) % 8)

    assert port_list(decode(bytes(raw))) == tuple(ports)


def test_port_list_reads_the_hex_spelling() -> None:
    # First octet ports 1-8, most significant bit lowest numbered port (RFC 4363).
    assert port_list("C0:80") == (1, 2, 9)


def test_port_list_reads_the_printable_spelling() -> None:
    # 0x60 is '`'. A reader assuming hex would report this VLAN as having no members at all.
    assert port_list("`") == (2, 3)


def test_port_list_prefers_text_where_the_hex_reading_could_not_have_been_written() -> None:
    # "41" spells hex 0x41, but 0x41 is 'A' and `decode` would have written it as "A" — so this
    # string can only have come from the two printable bytes 0x34 and 0x31.
    assert port_list("41") == port_list(decode(b"41"))
    assert port_list("41") == (3, 4, 6, 11, 12, 16)


def test_port_list_of_nothing_is_no_ports() -> None:
    assert port_list("") == ()
    assert port_list(None) == ()


# --- the static table ------------------------------------------------------------------------


async def test_read_vlans_reads_the_static_table_first() -> None:
    reading = await read_vlans(vlan_walk_session("core_sw_1"), max_rows=5000, max_records=512)

    assert reading.supported
    assert reading.table == STATIC_TABLE
    assert [vlan.vlan_id for vlan in reading.vlans] == [10, 20, 30, 40, 50]


async def test_read_vlans_carries_the_name_the_device_uses() -> None:
    reading = await read_vlans(vlan_walk_session("core_sw_1"), max_rows=5000, max_records=512)

    assert {vlan.vlan_id: vlan.name for vlan in reading.vlans} == {
        10: "Server VLAN",
        20: "Users VLAN",
        30: "Voice VLAN",
        40: "Guest VLAN",
        50: "IoT VLAN",
    }


async def test_read_vlans_resolves_every_member_port_to_an_interface() -> None:
    # The fixture's bridge ports are 11-16 and its interfaces are 1-6. A reader that treated a
    # bit position as an ifIndex would report members here rather than none, which is why the
    # numbering is offset.
    reading = await read_vlans(vlan_walk_session("core_sw_1"), max_rows=5000, max_records=512)

    by_id = {vlan.vlan_id: vlan for vlan in reading.vlans}

    assert by_id[10].if_indexes == (1, 2)
    assert by_id[20].if_indexes == (1, 2, 3)
    assert by_id[50].if_indexes == (1, 5, 6)


async def test_read_vlans_separates_the_untagged_members() -> None:
    reading = await read_vlans(vlan_walk_session("core_sw_1"), max_rows=5000, max_records=512)

    by_id = {vlan.vlan_id: vlan for vlan in reading.vlans}

    assert by_id[10].untagged_if_indexes == (1,)
    assert by_id[20].untagged_if_indexes == (3,)
    assert by_id[50].untagged_if_indexes == (5, 6)


async def test_read_vlans_accepts_a_vlan_with_no_untagged_ports() -> None:
    # A trunk-only VLAN. The column is a zero-length octet string, not an absent one.
    reading = await read_vlans(vlan_walk_session("core_sw_1"), max_rows=5000, max_records=512)

    voice = next(vlan for vlan in reading.vlans if vlan.vlan_id == 30)

    assert voice.if_indexes == (2, 3)
    assert voice.untagged_if_indexes == ()


async def test_read_vlans_drops_a_row_that_is_not_active() -> None:
    # `notReady` is a VLAN being built or taken out. Recording it would put a VLAN in the
    # inventory that the bridge is not running.
    reading = await read_vlans(vlan_walk_session("core_sw_1"), max_rows=5000, max_records=512)

    assert all(vlan.vlan_id != 99 for vlan in reading.vlans)


def test_parse_static_keeps_a_row_that_reports_no_status_at_all() -> None:
    # Plenty of agents omit the column, and treating silence as inactive would empty the table.
    records = parse_static(
        {
            "1.3.6.1.2.1.17.7.1.4.3.1.1.10": "Server VLAN",
            "1.3.6.1.2.1.17.7.1.4.3.1.2.10": "80",
        },
        {1: 7},
    )

    assert [record.vlan_id for record in records] == [10]
    assert records[0].if_indexes == (7,)


def test_parse_static_drops_an_index_that_is_not_a_vlan_id() -> None:
    # VlanIndex is 1..4094. Anything else is a row this reader has no business inventing a VLAN
    # from.
    records = parse_static(
        {
            "1.3.6.1.2.1.17.7.1.4.3.1.1.0": "reserved",
            "1.3.6.1.2.1.17.7.1.4.3.1.1.4095": "reserved",
            "1.3.6.1.2.1.17.7.1.4.3.1.1.1.1": "not one number",
            "1.3.6.1.2.1.17.7.1.4.3.1.1.20": "Users VLAN",
        },
        {},
    )

    assert [record.vlan_id for record in records] == [20]


# --- the printable spelling, on a real fixture -----------------------------------------------


async def test_read_vlans_reads_bitmaps_that_decode_as_text() -> None:
    reading = await read_vlans(vlan_walk_session("acc_sw_1"), max_rows=5000, max_records=512)

    by_id = {vlan.vlan_id: vlan for vlan in reading.vlans}

    assert by_id[20].if_indexes == (102, 103)
    assert by_id[20].untagged_if_indexes == (102, 103)


async def test_read_vlans_treats_membership_as_the_union_of_both_bitmaps() -> None:
    # VLAN 30's untagged bitmap names a port its egress bitmap does not, which an agent should
    # not do and some do. The union is what membership means, so the port is kept.
    reading = await read_vlans(vlan_walk_session("acc_sw_1"), max_rows=5000, max_records=512)

    voice = next(vlan for vlan in reading.vlans if vlan.vlan_id == 30)

    assert voice.if_indexes == (102, 104)
    assert voice.untagged_if_indexes == (102, 104)


# --- the fallback ----------------------------------------------------------------------------


async def test_read_vlans_falls_back_to_the_current_table() -> None:
    reading = await read_vlans(vlan_walk_session("legacy_current"), max_rows=5000, max_records=512)

    assert reading.supported
    assert reading.table == CURRENT_TABLE
    assert [vlan.vlan_id for vlan in reading.vlans] == [20, 50]


async def test_the_current_table_carries_no_names() -> None:
    reading = await read_vlans(vlan_walk_session("legacy_current"), max_rows=5000, max_records=512)

    assert all(vlan.name is None for vlan in reading.vlans)


async def test_the_newest_time_mark_wins_for_one_vlan() -> None:
    # The index is timeMark.vlanId and the table can hold several ages of one VLAN. NetShield
    # reads the whole table every time, so the older entry is discarded rather than merged.
    reading = await read_vlans(vlan_walk_session("legacy_current"), max_rows=5000, max_records=512)

    users = next(vlan for vlan in reading.vlans if vlan.vlan_id == 20)

    assert users.if_indexes == (1, 2)


def test_parse_current_drops_an_index_that_is_not_a_time_mark_and_a_vlan() -> None:
    records = parse_current(
        {
            "1.3.6.1.2.1.17.7.1.4.2.1.4.20": "80",
            "1.3.6.1.2.1.17.7.1.4.2.1.4.0.20.1": "80",
            "1.3.6.1.2.1.17.7.1.4.2.1.4.0.30": "80",
        },
        {1: 1},
    )

    assert [record.vlan_id for record in records] == [30]


# --- the ports the device cannot place -------------------------------------------------------


async def test_a_bridge_port_the_device_did_not_map_is_counted_and_dropped() -> None:
    reading = await read_vlans(
        vlan_walk_session("unresolvable_ports"), max_rows=5000, max_records=512
    )

    vlan = reading.vlans[0]

    assert vlan.if_indexes == (7,)
    assert vlan.port_count == 3
    assert vlan.unresolved_port_count == 2


# --- the absence -----------------------------------------------------------------------------


async def test_a_device_with_neither_table_is_not_a_device_with_no_vlans() -> None:
    # The distinction the API withdraws on. A router implements no VLAN table and reporting that
    # as an empty inventory would delete every VLAN NetShield knew about the moment it read one.
    reading = await read_vlans(vlan_walk_session("no_vlans"), max_rows=5000, max_records=512)

    assert reading.supported is False
    assert reading.vlans == ()
    assert reading.table is None


async def test_a_reading_says_when_it_was_cut_short() -> None:
    reading = await read_vlans(vlan_walk_session("core_sw_1"), max_rows=5000, max_records=2)

    assert reading.truncated
    assert [vlan.vlan_id for vlan in reading.vlans] == [10, 20]


async def test_a_reading_that_fitted_is_not_marked_truncated() -> None:
    reading = await read_vlans(vlan_walk_session("core_sw_1"), max_rows=5000, max_records=5)

    assert reading.truncated is False
    assert len(reading.vlans) == 5


def test_the_fixture_estate_carries_the_five_vlans_the_dashboard_names() -> None:
    # The fixture and the estate `VlanInventoryTests` asserts against are the same network. If
    # one is renamed the other should be too, and this is what says so.
    values = vlan_walk_fixture("core_sw_1")

    names = {
        oid.rsplit(".", 1)[1]: value
        for oid, value in values.items()
        if oid.startswith("1.3.6.1.2.1.17.7.1.4.3.1.1.")
    }

    assert names["10"] == "Server VLAN"
    assert names["20"] == "Users VLAN"
    assert names["30"] == "Voice VLAN"
    assert names["40"] == "Guest VLAN"
    assert names["50"] == "IoT VLAN"
