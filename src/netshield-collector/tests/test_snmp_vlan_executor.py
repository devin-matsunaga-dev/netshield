"""The VLAN walk end to end, and the failure isolation that is the reason it is its own walk."""

from __future__ import annotations

from collections.abc import AsyncIterator, Sequence
from contextlib import asynccontextmanager
from typing import Any

import pytest

from collector.models import CredentialKind, LeasedJob
from collector.snmp.session import FixtureSession, SnmpError, SnmpSession
from collector.snmp.vlans import VlanWalkExecutor, VlanWalkJobError
from tests.conftest import snmp_credential, vlan_job, vlan_walk_fixture


def session_factory(name: str, *, seen: list[Any] | None = None) -> Any:
    values = vlan_walk_fixture(name)

    @asynccontextmanager
    async def factory(
        address: str,
        credential: Any,
        *,
        timeout_seconds: float,
        retries: int,
        max_repetitions: int,
    ) -> AsyncIterator[SnmpSession]:
        if seen is not None:
            seen.append((address, credential, timeout_seconds, retries, max_repetitions))

        yield FixtureSession(values)

    return factory


# --- the payload -----------------------------------------------------------------------------


async def test_execute_reports_the_devices_vlan_inventory() -> None:
    payload = await VlanWalkExecutor(session_factory("core_sw_1")).execute(vlan_job())

    assert payload["walk"] == "vlans"
    assert payload["vlansSupported"] is True
    assert payload["vlanTable"] == "dot1qVlanStatic"
    assert payload["vlanCount"] == 5
    assert payload["vlansTruncated"] is False


async def test_execute_writes_every_member_the_api_reads() -> None:
    # The payload's field names are the contract. This is the test that fails when one moves.
    payload = await VlanWalkExecutor(session_factory("core_sw_1")).execute(vlan_job())

    assert set(payload) == {
        "walk",
        "vlansSupported",
        "vlanTable",
        "vlanCount",
        "vlansTruncated",
        "vlans",
    }

    assert set(payload["vlans"][0]) == {
        "vlanId",
        "name",
        "ifIndexes",
        "untaggedIfIndexes",
        "portCount",
        "unresolvedPortCount",
    }


async def test_execute_carries_the_membership_a_vlan_tile_is_counted_from() -> None:
    payload = await VlanWalkExecutor(session_factory("core_sw_1")).execute(vlan_job())

    assert payload["vlans"][0] == {
        "vlanId": 10,
        "name": "Server VLAN",
        "ifIndexes": [1, 2],
        "untaggedIfIndexes": [1],
        "portCount": 2,
        "unresolvedPortCount": 0,
    }


async def test_execute_carries_no_port_names() -> None:
    # Unlike a neighbour's far end, these are the observing device's own ports and the API
    # already holds a name for every one of them in `device_interfaces`. Sending 48 names per
    # VLAN per switch would be the same fact stored twice.
    payload = await VlanWalkExecutor(session_factory("core_sw_1")).execute(vlan_job())

    assert all("localPortName" not in vlan for vlan in payload["vlans"])


async def test_execute_against_a_device_with_no_vlan_table_reports_unsupported() -> None:
    payload = await VlanWalkExecutor(session_factory("no_vlans")).execute(vlan_job())

    assert payload["vlansSupported"] is False
    assert payload["vlans"] == []
    assert payload["vlanTable"] is None


async def test_execute_reports_the_fallback_table_by_name() -> None:
    payload = await VlanWalkExecutor(session_factory("legacy_current")).execute(vlan_job())

    assert payload["vlanTable"] == "dot1qVlanCurrent"
    assert [vlan["name"] for vlan in payload["vlans"]] == [None, None]


async def test_execute_says_when_the_reading_was_cut_short() -> None:
    payload = await VlanWalkExecutor(session_factory("core_sw_1")).execute(
        vlan_job(
            parameters={
                "walk": "vlans",
                "timeoutSeconds": 2.0,
                "retries": 1,
                "maxRepetitions": 25,
                "maxRows": 5000,
                "maxVlans": 3,
            }
        )
    )

    assert payload["vlansTruncated"] is True
    assert payload["vlanCount"] == 3


# --- failure isolation -----------------------------------------------------------------------


async def test_a_vlan_table_that_never_answers_fails_only_this_job() -> None:
    """The reason the VLAN read is a walk of its own rather than part of the neighbour walk.

    Q-BRIDGE is the table set an old or half-implemented agent is likeliest to hang on. When it
    does, *this* job fails; the neighbour walk is a different row in ``collector_jobs`` with a
    different result, and the edges it established are untouched.
    """

    class DyingSession:
        async def get(self, oids: Sequence[str]) -> dict[str, str]:
            return {}

        async def walk(self, root: str, *, max_rows: int) -> dict[str, str]:
            if root.startswith("1.3.6.1.2.1.17.7.1.4"):
                raise SnmpError("192.0.2.10 stopped answering partway through the VLAN table.")

            return {}

    @asynccontextmanager
    async def factory(
        address: str,
        credential: Any,
        *,
        timeout_seconds: float,
        retries: int,
        max_repetitions: int,
    ) -> AsyncIterator[SnmpSession]:
        yield DyingSession()

    with pytest.raises(SnmpError):
        await VlanWalkExecutor(factory).execute(vlan_job())


# --- the refusals ----------------------------------------------------------------------------


async def test_execute_with_no_device_fails_the_job() -> None:
    job = LeasedJob.model_validate(
        vlan_job().model_dump(mode="json", by_alias=True) | {"device": None}
    )

    with pytest.raises(VlanWalkJobError, match="needs a device"):
        await VlanWalkExecutor(session_factory("core_sw_1")).execute(job)


async def test_execute_with_no_credential_fails_the_job() -> None:
    job = LeasedJob.model_validate(
        vlan_job().model_dump(mode="json", by_alias=True) | {"credential": None}
    )

    with pytest.raises(VlanWalkJobError, match="needs a credential"):
        await VlanWalkExecutor(session_factory("core_sw_1")).execute(job)


async def test_execute_with_another_walks_parameters_refuses_rather_than_reading_anything() -> None:
    job = vlan_job(
        parameters={
            "walk": "neighbors",
            "timeoutSeconds": 2.0,
            "retries": 1,
            "maxRepetitions": 25,
            "maxRows": 5000,
            "maxVlans": 512,
        }
    )

    with pytest.raises(VlanWalkJobError, match="names neighbors"):
        await VlanWalkExecutor(session_factory("core_sw_1")).execute(job)


async def test_execute_with_parameters_of_the_wrong_shape_refuses_at_once() -> None:
    job = vlan_job(parameters={"walk": "vlans", "maxVlans": 0})

    with pytest.raises(VlanWalkJobError, match="not a VLAN walk"):
        await VlanWalkExecutor(session_factory("core_sw_1")).execute(job)


async def test_execute_with_no_parameters_fails_the_job() -> None:
    job = LeasedJob.model_validate(
        vlan_job().model_dump(mode="json", by_alias=True) | {"parameters": None}
    )

    with pytest.raises(VlanWalkJobError, match="no parameters"):
        await VlanWalkExecutor(session_factory("core_sw_1")).execute(job)


# --- the lease -------------------------------------------------------------------------------


async def test_execute_reads_with_the_credential_the_lease_delivered() -> None:
    seen: list[Any] = []
    credential = snmp_credential(kind=CredentialKind.SNMP_V2C)

    await VlanWalkExecutor(session_factory("core_sw_1", seen=seen)).execute(
        vlan_job(credential=credential, address="198.51.100.7")
    )

    assert seen == [("198.51.100.7", credential, 2.0, 1, 25)]


async def test_execute_declares_the_discriminator_the_api_writes() -> None:
    assert VlanWalkExecutor().walk == "vlans"


async def test_the_vlan_walk_asks_no_vendor_anything() -> None:
    # It takes no vendor registry, because Q-BRIDGE-MIB is a standard and there is no vendor
    # decision to make. A private VLAN MIB would be a vendor decision and would go behind the
    # adapter, which is where CDP went.
    assert VlanWalkExecutor().walk == "vlans"

    executor = VlanWalkExecutor(session_factory("core_sw_1"))

    assert not hasattr(executor, "_vendors")
