"""Reading a decoded octet string back, and the two capability columns that need it.

``session.decode`` renders an octet string as text when every byte is printable and as
colon-separated hex otherwise, because ``ifDescr`` and ``ifPhysAddress`` are the same SNMP type
and only one of them is a word. A capability map was never a word, so it has to undo that
choice before it can be read — and it has to undo it the *right* way round for its column, since
``lldpRemSysCapEnabled`` is ``BITS`` numbered from the most significant bit and
``cdpCacheCapabilities`` is a big-endian integer numbered from the least.

Reading either with the other's rule names the wrong capabilities rather than failing, which is
the whole reason these are two functions.
"""

from __future__ import annotations

import pytest

from collector.snmp import oids
from collector.snmp.cdp import parse_cache
from collector.snmp.lldp import parse_local_ports, parse_remote
from collector.snmp.octets import big_endian_int, bit_positions, octets
from collector.snmp.ports import PortNames, build
from collector.snmp.session import decode
from tests.conftest import topology_walk_fixture

# IEEE 802.1AB LldpSystemCapabilitiesMap, by name.
OTHER, REPEATER, BRIDGE, WLAN_AP, ROUTER, TELEPHONE, DOCSIS, STATION = range(8)

IF_DESCR = "1.3.6.1.2.1.2.2.1.2"


# --- the inversion ---------------------------------------------------------------------------


@pytest.mark.parametrize("raw", [b"\x28\x00", b"\x30\x00", b"\x01\x00", b"\x00\x00\x00\x0b", b"A"])
def test_octets_reads_back_whatever_decode_wrote(raw: bytes) -> None:
    """The two have to stay each other's inverse, whichever spelling `decode` chose."""
    assert octets(decode(raw)) == raw


def test_octets_of_nothing_is_no_bytes() -> None:
    assert octets("") == b""
    assert octets(None) == b""


# --- the BITS reading ------------------------------------------------------------------------


@pytest.mark.parametrize(
    ("value", "expected"),
    [
        ("28:00", {BRIDGE, ROUTER}),
        ("30:00", {BRIDGE, WLAN_AP}),
        ("24:00", {BRIDGE, TELEPHONE}),
        ("01:00", {STATION}),
        ("80:00", {OTHER}),
        ("02:00", {DOCSIS}),
        ("00:00", set()),
    ],
)
def test_bit_positions_numbers_from_the_most_significant_bit(
    value: str, expected: set[int]
) -> None:
    """`BITS` bit 0 is `0x80` of octet 0, not `0x01`.

    A bridge-and-router neighbour advertises `28:00`. Read from the other end it would be bits 3
    and 5 — wlanAccessPoint and telephone — which is a switch reported as a desk phone.
    """
    mask = bit_positions(value, max_octets=oids.LLDP_SYS_CAP_OCTETS)

    assert mask is not None
    assert {bit for bit in range(16) if mask & (1 << bit)} == expected


def test_bit_positions_reads_the_second_octet() -> None:
    assert bit_positions("00:80", max_octets=2) == 1 << 8


def test_bit_positions_of_a_value_longer_than_its_column_is_nothing() -> None:
    """A reading that does not fit the shape the MIB declares is dropped, not guessed at."""
    assert bit_positions("28:00:00:00", max_octets=oids.LLDP_SYS_CAP_OCTETS) is None


def test_bit_positions_of_nothing_is_nothing() -> None:
    assert bit_positions(None, max_octets=2) is None
    assert bit_positions("", max_octets=2) is None


# --- the big-endian reading ------------------------------------------------------------------


@pytest.mark.parametrize(
    ("value", "expected"),
    [
        ("00:00:00:0B", 0x0B),
        ("00:00:00:01", 0x01),
        ("00:00:00:29", 0x29),
        ("00:00:00:00", 0),
    ],
)
def test_big_endian_int_numbers_from_the_least_significant_bit(value: str, expected: int) -> None:
    assert big_endian_int(value, max_octets=oids.CDP_CACHE_CAPABILITY_OCTETS) == expected


def test_big_endian_int_of_a_value_longer_than_its_column_is_nothing() -> None:
    assert big_endian_int("00:00:00:00:0B", max_octets=oids.CDP_CACHE_CAPABILITY_OCTETS) is None


def test_the_two_readings_disagree_about_one_value_and_that_is_the_point() -> None:
    """`28:00` is bridge+router as BITS and 10240 as an integer. One column each."""
    assert bit_positions("28:00", max_octets=2) == (1 << BRIDGE) | (1 << ROUTER)
    assert big_endian_int("28:00", max_octets=4) == 0x2800


# --- through the real readers ----------------------------------------------------------------


def test_lldp_reads_a_capability_map_from_a_recorded_walk() -> None:
    """The regression the fixtures used to hide: this column is not `int(value)`.

    `28:00` is not a decimal number, so the previous reading returned `None` for every real
    agent while the hand-authored fixtures — which spelled it `28` — kept passing.
    """
    walked = topology_walk_fixture("edge_sw_endpoints")
    ports = build({key: value for key, value in walked.items() if key.startswith(IF_DESCR)}, {})

    by_port = {
        neighbor.local_if_index: neighbor
        for neighbor in parse_remote(walked, parse_local_ports(walked), ports, {})
    }

    assert by_port[1].capabilities == (1 << BRIDGE) | (1 << ROUTER)
    assert by_port[2].capabilities == (1 << BRIDGE) | (1 << WLAN_AP)
    assert by_port[3].capabilities == (1 << BRIDGE) | (1 << TELEPHONE)
    assert by_port[4].capabilities == 1 << STATION


def test_cdp_reads_a_capability_word_from_a_recorded_walk() -> None:
    walked = topology_walk_fixture("acc_sw_1")
    ports: PortNames = build(
        {key: value for key, value in walked.items() if key.startswith(IF_DESCR)}, {}
    )

    neighbors = parse_cache(walked, ports)

    assert [neighbor.capabilities for neighbor in neighbors] == [0x0B]
