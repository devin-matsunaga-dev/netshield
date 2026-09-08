"""The neighbour walk end to end: a leased job in, the API's payload shape out.

Two things are pinned here that live nowhere else. The payload's field names, because the two
sides of that shape are in two repositories with no generator between them — a member renamed
here should break a test rather than quietly stop being read by ``RecordNeighborWalkResultHandler``.
And *which protocols a vendor is asked for*, because that is the whole of the vendor seam this
walk depends on: shared code asks the adapter and never learns a vendor's name.
"""

from __future__ import annotations

from collections.abc import AsyncIterator
from contextlib import asynccontextmanager
from typing import Any

import pytest

from collector.models import CredentialKind, LeasedJob
from collector.snmp.neighbors import NeighborWalkExecutor, NeighborWalkJobError
from collector.snmp.session import FixtureSession, SnmpSession
from collector.vendors import VendorRegistry, snmp_adapters
from tests.conftest import neighbor_job, snmp_credential, topology_walk_fixture

VENDORS = VendorRegistry(snmp_adapters())


def session_factory(name: str, *, seen: list[Any] | None = None) -> Any:
    """A session factory replaying one recorded topology walk, ignoring the address."""
    values = topology_walk_fixture(name)

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


def executor(name: str, **kwargs: Any) -> NeighborWalkExecutor:
    return NeighborWalkExecutor(VENDORS, session_factory(name, **kwargs))


# --- the payload -----------------------------------------------------------------------------


async def test_execute_reads_both_protocols_on_a_cisco_access_switch() -> None:
    payload = await executor("acc_sw_1").execute(neighbor_job(vendor="CiscoIos"))

    assert payload["walk"] == "neighbors"
    assert payload["lldpSupported"] is True
    assert payload["cdpSupported"] is True
    assert payload["lldpCount"] == 1
    assert payload["cdpCount"] == 1


async def test_execute_writes_every_member_the_api_reads() -> None:
    # The payload's field names are the contract. This is the test that fails when one moves.
    payload = await executor("acc_sw_1").execute(neighbor_job(vendor="CiscoIos"))

    assert set(payload) == {
        "walk",
        "localChassisId",
        "localChassisIdKind",
        "localSystemName",
        "lldpSupported",
        "lldpCount",
        "lldpTruncated",
        "lldp",
        "cdpSupported",
        "cdpCount",
        "cdpTruncated",
        "cdp",
    }

    assert set(payload["lldp"][0]) == {
        "localIfIndex",
        "localPortName",
        "chassisId",
        "chassisIdKind",
        "portId",
        "portIdKind",
        "portDescription",
        "systemName",
        "systemDescription",
        "managementAddress",
        "capabilities",
    }

    assert set(payload["cdp"][0]) == {
        "localIfIndex",
        "localPortName",
        "deviceId",
        "devicePort",
        "platform",
        "version",
        "address",
        "capabilities",
    }


async def test_execute_reports_the_devices_own_advertised_identity() -> None:
    # The other half of every edge: a neighbour's chassis id is matched against this value on
    # the device that advertised it, which is how two ends of one cable become one link.
    payload = await executor("core_sw_1").execute(neighbor_job(vendor="AristaEos"))

    assert payload["localChassisId"] == "00:1C:73:00:00:01"
    assert payload["localChassisIdKind"] == "MacAddress"
    assert payload["localSystemName"] == "core-sw-1"


async def test_execute_reports_a_conflict_as_two_observations_rather_than_resolving_it() -> None:
    # Deciding which of the two is the edge is the API's, in AdjacencyRule. The collector's job
    # is to report what each protocol actually said.
    payload = await executor("conflicting_neighbors").execute(neighbor_job(vendor="CiscoIos"))

    assert payload["lldp"][0]["systemName"] == "core-sw-1"
    assert payload["cdp"][0]["deviceId"] == "ghost-sw-7"
    assert payload["lldp"][0]["localIfIndex"] == payload["cdp"][0]["localIfIndex"] == 1


# --- the vendor seam -------------------------------------------------------------------------


async def test_execute_asks_a_cisco_for_cdp() -> None:
    payload = await executor("acc_sw_1").execute(neighbor_job(vendor="CiscoIos"))

    assert payload["cdpSupported"] is True


async def test_execute_does_not_ask_a_non_cisco_for_cdp_even_when_the_table_is_there() -> None:
    """The fixture holds a CDP cache. An Arista is never asked for one, so it is never read."""
    payload = await executor("acc_sw_1").execute(neighbor_job(vendor="AristaEos"))

    assert payload["cdpSupported"] is False
    assert payload["cdp"] == []
    assert payload["lldpSupported"] is True


async def test_execute_asks_a_generic_snmp_device_for_lldp_alone() -> None:
    payload = await executor("acc_sw_1").execute(neighbor_job(vendor="GenericSnmp"))

    assert payload["lldpSupported"] is True
    assert payload["cdpSupported"] is False


async def test_execute_asks_for_lldp_when_no_adapter_is_registered_at_all() -> None:
    walk = NeighborWalkExecutor(VendorRegistry([]), session_factory("acc_sw_1"))

    payload = await walk.execute(neighbor_job(vendor="CiscoIos"))

    assert payload["lldpSupported"] is True
    assert payload["cdpSupported"] is False


async def test_execute_asks_for_lldp_when_the_device_names_no_vendor() -> None:
    device = neighbor_job().device
    assert device is not None

    payload = await executor("acc_sw_1").execute(
        neighbor_job(device=device.model_copy(update={"vendor": None}))
    )

    assert payload["lldpSupported"] is True
    assert payload["cdpSupported"] is False


# --- the absences ----------------------------------------------------------------------------


async def test_execute_against_a_device_with_neither_protocol_reports_both_unsupported() -> None:
    # The distinction that stops the API withdrawing every edge on a device that simply has
    # neither protocol turned on.
    payload = await executor("no_neighbors").execute(neighbor_job(vendor="CiscoIos"))

    assert payload["lldpSupported"] is False
    assert payload["cdpSupported"] is False
    assert payload["lldp"] == []
    assert payload["cdp"] == []


# --- the refusals ----------------------------------------------------------------------------


async def test_execute_with_no_device_fails_the_job() -> None:
    job = LeasedJob.model_validate(
        neighbor_job().model_dump(mode="json", by_alias=True) | {"device": None}
    )

    with pytest.raises(NeighborWalkJobError, match="needs a device"):
        await executor("acc_sw_1").execute(job)


async def test_execute_with_no_credential_fails_the_job() -> None:
    job = LeasedJob.model_validate(
        neighbor_job().model_dump(mode="json", by_alias=True) | {"credential": None}
    )

    with pytest.raises(NeighborWalkJobError, match="needs a credential"):
        await executor("acc_sw_1").execute(job)


async def test_execute_with_another_walks_parameters_refuses_rather_than_reading_anything() -> None:
    job = neighbor_job(
        parameters={
            "walk": "clients",
            "timeoutSeconds": 2.0,
            "retries": 1,
            "maxRepetitions": 25,
            "maxRows": 5000,
            "maxNeighbors": 1000,
        }
    )

    with pytest.raises(NeighborWalkJobError, match="names clients"):
        await executor("acc_sw_1").execute(job)


async def test_execute_with_parameters_of_the_wrong_shape_refuses_at_once() -> None:
    job = neighbor_job(parameters={"walk": "neighbors", "timeoutSeconds": -1})

    with pytest.raises(NeighborWalkJobError, match="not a neighbour walk"):
        await executor("acc_sw_1").execute(job)


async def test_execute_with_no_parameters_fails_the_job() -> None:
    job = LeasedJob.model_validate(
        neighbor_job().model_dump(mode="json", by_alias=True) | {"parameters": None}
    )

    with pytest.raises(NeighborWalkJobError, match="no parameters"):
        await executor("acc_sw_1").execute(job)


# --- the lease -------------------------------------------------------------------------------


async def test_execute_reads_with_the_credential_the_lease_delivered() -> None:
    seen: list[Any] = []
    credential = snmp_credential(kind=CredentialKind.SNMP_V2C)

    await executor("acc_sw_1", seen=seen).execute(
        neighbor_job(credential=credential, address="198.51.100.7")
    )

    assert seen == [("198.51.100.7", credential, 2.0, 1, 25)]


async def test_execute_declares_the_discriminator_the_api_writes() -> None:
    assert NeighborWalkExecutor(VENDORS).walk == "neighbors"
