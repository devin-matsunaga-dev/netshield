"""The interface-name map every neighbour reading joins through."""

from __future__ import annotations

import pytest

from collector.snmp.ports import build, read_port_names
from collector.snmp.session import FixtureSession
from tests.conftest import topology_walk_session

IF_DESCR = "1.3.6.1.2.1.2.2.1.2"
IF_NAME = "1.3.6.1.2.1.31.1.1.1.1"


def test_build_prefers_if_name_over_if_descr_for_the_display_name() -> None:
    ports = build({f"{IF_DESCR}.1": "GigabitEthernet0/1"}, {f"{IF_NAME}.1": "Gi0/1"})

    assert ports.name_for(1) == "Gi0/1"


def test_build_indexes_both_spellings_so_either_resolves() -> None:
    ports = build({f"{IF_DESCR}.1": "GigabitEthernet0/1"}, {f"{IF_NAME}.1": "Gi0/1"})

    assert ports.index_for("Gi0/1") == 1
    assert ports.index_for("GigabitEthernet0/1") == 1


def test_build_matches_a_name_regardless_of_case() -> None:
    ports = build({f"{IF_DESCR}.7": "Ethernet7"}, {})

    assert ports.index_for("ethernet7") == 7
    assert ports.index_for("  ETHERNET7 ") == 7


def test_build_falls_back_to_if_descr_when_the_device_has_no_if_x_table() -> None:
    ports = build({f"{IF_DESCR}.3": "FastEthernet0/3"}, {})

    assert ports.name_for(3) == "FastEthernet0/3"
    assert ports.knows(3)


def test_build_drops_an_index_that_is_not_a_number() -> None:
    ports = build({f"{IF_DESCR}.notanindex": "Nonsense"}, {})

    assert ports.names == {}


def test_build_drops_an_empty_value() -> None:
    ports = build({f"{IF_DESCR}.1": "   "}, {})

    assert not ports.knows(1)


def test_build_keeps_the_lower_index_when_two_interfaces_share_a_name() -> None:
    ports = build({f"{IF_DESCR}.9": "Duplicate", f"{IF_DESCR}.4": "Duplicate"}, {})

    assert ports.index_for("Duplicate") == 4


def test_index_for_answers_nothing_for_a_name_the_device_never_gave() -> None:
    ports = build({f"{IF_DESCR}.1": "Gi0/1"}, {})

    assert ports.index_for("Te1/1/1") is None
    assert ports.index_for(None) is None


def test_knows_is_false_for_an_index_the_device_never_reported() -> None:
    ports = build({f"{IF_DESCR}.1": "Gi0/1"}, {})

    assert not ports.knows(99)
    assert not ports.knows(None)


@pytest.mark.asyncio
async def test_read_port_names_reads_both_columns_from_a_recorded_walk() -> None:
    ports = await read_port_names(topology_walk_session("core_sw_1"), max_rows=5_000)

    assert ports.names == {1: "Et1", 2: "Et2", 3: "Ma1"}
    assert ports.index_for("Ethernet2") == 2


@pytest.mark.asyncio
async def test_read_port_names_is_empty_rather_than_raising_when_nothing_answers() -> None:
    ports = await read_port_names(FixtureSession({}), max_rows=5_000)

    assert ports.names == {}
    assert ports.by_name == {}
