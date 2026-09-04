"""Fingerprinting, against one recorded walk per platform SPEC.md §4 names.

This is the WP-1.5 criterion stated as a test: *a walk against recorded fixtures for each
supported vendor produces the correct fingerprint, and an unrecognised device lands as generic
SNMP with reduced capability flagged.* Every expectation below is derived by hand from the
fixture's own ``sysDescr``, ``sysObjectID`` and private OIDs, not from running the resolver.
"""

from __future__ import annotations

import pytest

from collector.snmp.facts import SystemGroup, chassis, physical_entities
from collector.snmp.fingerprint import walk_device
from collector.snmp.session import SnmpError, SnmpSession
from collector.vendors import VendorRegistry, snmp_adapters
from tests.conftest import walk_fixture, walk_session

LIMITS = {"max_rows": 5000, "max_interfaces": 500}


def registry() -> VendorRegistry:
    return VendorRegistry(snmp_adapters())


async def fingerprint(name: str) -> object:
    return await walk_device(walk_session(name), registry(), **LIMITS)


@pytest.mark.parametrize(
    ("fixture", "vendor", "model", "os_version", "serial"),
    [
        ("cisco_ios", "CiscoIos", "WS-C2960X-48FPD-L", "15.2(7)E3", "FOC1234X5YZ"),
        ("cisco_nxos", "CiscoNxOs", "N9K-C93180YC-EX", "9.3(10)", "SAL1234ABCD"),
        (
            "juniper_junos",
            "JuniperJunOs",
            "Juniper EX4300-48T Ethernet Switch",
            "21.4R3.15",
            "JN123456789",
        ),
        ("arista_eos", "AristaEos", "DCS-7050SX3-48YC8", "4.29.2F", "JPE12345678"),
        (
            "fortinet_fortios",
            "FortinetFortiOs",
            "FortiGate-100F",
            "v7.2.5,build1517,230606 (GA.F)",
            "FG100FTK20001234",
        ),
        (
            "mikrotik_routeros",
            "MikroTikRouterOs",
            "CCR2004-1G-12S+2XS",
            "7.11.2",
            "HDX08ABCDEF",
        ),
    ],
)
async def test_each_supported_vendor_is_fingerprinted_from_its_recorded_walk(
    fixture: str,
    vendor: str,
    model: str,
    os_version: str,
    serial: str,
) -> None:
    outcome = await walk_device(walk_session(fixture), registry(), **LIMITS)

    assert outcome.vendor == vendor
    assert outcome.facts.model == model
    assert outcome.facts.os_version == os_version
    assert outcome.facts.serial_number == serial
    assert outcome.reduced_capability is False


async def test_an_unrecognised_device_lands_as_generic_snmp_with_reduced_capability() -> None:
    """SPEC.md §4: the fallback, and the flag the UI has to be able to label it with."""
    outcome = await walk_device(walk_session("unrecognised"), registry(), **LIMITS)

    assert outcome.vendor == "GenericSnmp"
    assert outcome.reduced_capability is True

    # It is a fallback, not a failure: the standard MIBs still answered, so the three facts are
    # known even though the platform is not.
    assert outcome.facts.model == "ESW-2400"
    assert outcome.facts.os_version == "2.1.4"
    assert outcome.facts.serial_number == "EXN0001234"


async def test_the_system_group_is_carried_through_whole() -> None:
    outcome = await walk_device(walk_session("cisco_ios"), registry(), **LIMITS)

    assert outcome.system.name == "lab-sw-ios-01"
    assert outcome.system.object_id == "1.3.6.1.4.1.9.1.2494"
    assert outcome.system.location == "Lab rack 3"
    assert outcome.system.uptime_seconds == 1234567.89


async def test_a_walk_reports_every_interface_the_device_answered_for() -> None:
    outcome = await walk_device(walk_session("cisco_ios"), registry(), **LIMITS)

    assert outcome.interface_count == 2
    assert outcome.interfaces_truncated is False
    assert [record.index for record in outcome.interfaces] == [1, 2]


async def test_more_interfaces_than_the_job_allows_are_truncated_and_said_to_be() -> None:
    """A 500-port chassis must not be able to make one result unbounded."""
    outcome = await walk_device(
        walk_session("cisco_ios"),
        registry(),
        max_rows=5000,
        max_interfaces=1,
    )

    assert len(outcome.interfaces) == 1
    assert outcome.interface_count == 2
    assert outcome.interfaces_truncated is True


class SilentSession:
    """A device that accepts every read and answers nothing."""

    async def get(self, oids: object) -> dict[str, str]:
        return {}

    async def walk(self, root: str, *, max_rows: int) -> dict[str, str]:
        return {}


async def test_a_device_with_no_system_group_is_a_failure_not_a_generic_device() -> None:
    """Overwriting a known fingerprint with "nothing" would lose what an earlier walk found."""
    session: SnmpSession = SilentSession()

    with pytest.raises(SnmpError, match="neither sysObjectID nor sysDescr"):
        await walk_device(session, registry(), **LIMITS)


async def test_with_no_adapters_registered_a_walk_fails_with_a_reason() -> None:
    with pytest.raises(SnmpError, match="no vendor adapters registered"):
        await walk_device(walk_session("cisco_ios"), VendorRegistry(), **LIMITS)


def test_the_chassis_row_is_the_lowest_indexed_one_of_class_chassis() -> None:
    entities = physical_entities(walk_fixture("arista_eos"))
    box = chassis(entities)

    assert box is not None
    assert box.serial_number == "JPE12345678"
    assert box.is_chassis


def test_a_device_with_no_entity_mib_has_no_chassis_row() -> None:
    assert chassis(physical_entities(walk_fixture("mikrotik_routeros"))) is None


def test_uptime_is_absent_rather_than_zero_when_the_device_did_not_answer() -> None:
    assert SystemGroup(descr="something").uptime_seconds is None
