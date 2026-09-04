"""The Discover executor: what it refuses, what it reports, and what a hung device costs.

The whole path runs here — parameters, credential, walk, payload — against a recorded walk
rather than a device, through the session factory the executor takes for exactly that reason.
"""

from __future__ import annotations

import asyncio
from collections.abc import Iterator
from contextlib import AbstractAsyncContextManager
from typing import Any

import httpx
import pytest
import respx

from collector.api import CollectorApi
from collector.config import CollectorSettings
from collector.jobs import ExecutorRegistry
from collector.models import CredentialKind, JobCredential
from collector.runner import CollectorRunner
from collector.snmp.executor import SnmpJobError, SnmpWalkExecutor
from collector.snmp.session import FixtureSession, SnmpSession
from collector.vendors import VendorRegistry, snmp_adapters
from tests.conftest import snmp_credential, walk_fixture, walk_job


def executor(fixture: str = "cisco_ios", *, hang: bool = False) -> SnmpWalkExecutor:
    """An executor whose sessions replay one recorded walk."""

    def factory(
        address: str,
        credential: JobCredential,
        *,
        timeout_seconds: float,
        retries: int,
        max_repetitions: int,
    ) -> AbstractAsyncContextManager[SnmpSession]:
        return FixtureSession(walk_fixture(fixture), hang=hang)

    return SnmpWalkExecutor(VendorRegistry(snmp_adapters()), factory)


async def test_a_walk_reports_the_fingerprint_and_the_interface_inventory() -> None:
    data = await executor().execute(walk_job())

    assert data["walk"] == "snmp"
    assert data["vendor"] == "CiscoIos"
    assert data["reducedCapability"] is False
    assert data["sysObjectId"] == "1.3.6.1.4.1.9.1.2494"
    assert data["sysName"] == "lab-sw-ios-01"
    assert data["model"] == "WS-C2960X-48FPD-L"
    assert data["osVersion"] == "15.2(7)E3"
    assert data["serialNumber"] == "FOC1234X5YZ"
    assert data["uptimeSeconds"] == 1234567.89
    assert data["interfaceCount"] == 2
    assert data["interfacesTruncated"] is False


async def test_the_payload_names_every_member_the_api_reads() -> None:
    """Two repositories, no generator between them. A rename here has to break a test here."""
    data = await executor().execute(walk_job())

    assert set(data) == {
        "walk",
        "vendor",
        "reducedCapability",
        "sysObjectId",
        "sysDescr",
        "sysName",
        "sysContact",
        "sysLocation",
        "uptimeSeconds",
        "model",
        "osVersion",
        "serialNumber",
        "interfaceCount",
        "interfacesTruncated",
        "interfaces",
    }

    assert set(data["interfaces"][0]) == {
        "index",
        "name",
        "description",
        "alias",
        "interfaceType",
        "mtu",
        "speedBitsPerSecond",
        "physicalAddress",
        "adminStatus",
        "operStatus",
    }


async def test_an_unrecognised_device_reports_reduced_capability() -> None:
    data = await executor("unrecognised").execute(walk_job())

    assert data["vendor"] == "GenericSnmp"
    assert data["reducedCapability"] is True


async def test_a_job_with_no_device_is_refused_before_a_session_is_opened() -> None:
    with pytest.raises(SnmpJobError, match="names none"):
        await executor().execute(walk_job().model_copy(update={"device": None}))


async def test_a_job_with_no_credential_is_refused() -> None:
    with pytest.raises(SnmpJobError, match="carries none"):
        await executor().execute(walk_job().model_copy(update={"credential": None}))


async def test_a_job_with_no_parameters_is_refused() -> None:
    with pytest.raises(SnmpJobError, match="no parameters"):
        await executor().execute(walk_job(parameters=None).model_copy(update={"parameters": None}))


async def test_a_discover_job_naming_another_walk_is_refused_rather_than_answered() -> None:
    """WP-1.6's range sweep is a Discover too. Each side has to recognise only its own."""
    with pytest.raises(SnmpJobError, match="names sweep"):
        await executor().execute(
            walk_job(
                parameters={
                    "walk": "sweep",
                    "timeoutSeconds": 2.0,
                    "retries": 1,
                    "maxRepetitions": 25,
                    "maxRows": 5000,
                    "maxInterfaces": 500,
                }
            )
        )


async def test_parameters_outside_their_bounds_are_refused() -> None:
    with pytest.raises(SnmpJobError, match="not an SNMP walk's"):
        await executor().execute(
            walk_job(
                parameters={
                    "walk": "snmp",
                    "timeoutSeconds": 0,
                    "retries": 1,
                    "maxRepetitions": 25,
                    "maxRows": 5000,
                    "maxInterfaces": 500,
                }
            )
        )


async def test_an_ssh_credential_fails_the_job_rather_than_being_coerced() -> None:
    from collector.snmp.session import SnmpError

    with pytest.raises(SnmpError, match="cannot authenticate"):
        await SnmpWalkExecutor(
            VendorRegistry(snmp_adapters()),
        ).execute(walk_job(credential=snmp_credential(kind=CredentialKind.SSH_KEY, community=None)))


# --- The batch is not stalled by one device (the WP-1.5 criterion) ----------------------------


def _lease(api_mock: respx.MockRouter, jobs: list[dict[str, Any]]) -> None:
    api_mock.get("/internal/collector/jobs").mock(
        return_value=httpx.Response(200, json={"jobs": jobs, "leaseSeconds": 300})
    )


def _submit(api_mock: respx.MockRouter) -> respx.Route:
    return api_mock.post("/internal/collector/results").mock(
        return_value=httpx.Response(200, json={"accepted": [], "duplicates": [], "rejected": []})
    )


class _HangingWalk(SnmpWalkExecutor):
    """The SNMP executor, with one device in the batch that accepts the read and never answers."""

    def __init__(self, stalls: set[str]) -> None:
        super().__init__(VendorRegistry(snmp_adapters()), self._factory)
        self._stalls = stalls

    def _factory(
        self,
        address: str,
        credential: JobCredential,
        *,
        timeout_seconds: float,
        retries: int,
        max_repetitions: int,
    ) -> AbstractAsyncContextManager[SnmpSession]:
        return FixtureSession(walk_fixture("cisco_ios"), hang=address in self._stalls)


def _jobs(addresses: Iterator[str]) -> list[dict[str, Any]]:
    return [
        walk_job(address=address).model_dump(by_alias=True, mode="json") for address in addresses
    ]


async def test_a_device_that_never_answers_does_not_stall_the_rest_of_the_batch(
    settings: CollectorSettings,
    api_mock: respx.MockRouter,
) -> None:
    """WP-1.5: *a timeout on one device does not stall the batch.*

    Three devices are leased together and the middle one never answers. The batch still comes
    back — two succeeded, one failed on the job timeout — and no slot is left occupied.
    """
    addresses = ["192.0.2.11", "192.0.2.12", "192.0.2.13"]

    _lease(api_mock, _jobs(iter(addresses)))
    submitted = _submit(api_mock)

    async with CollectorApi(settings) as api:
        runner = CollectorRunner(
            api,
            ExecutorRegistry([_HangingWalk({"192.0.2.12"})]),
            settings,
        )

        await asyncio.wait_for(runner.work_once(), timeout=10)

    body = submitted.calls.last.request.content.decode()

    assert body.count('"outcome":"Succeeded"') == 2
    assert body.count('"outcome":"Failed"') == 1
    assert "Timed out after" in body
    assert runner.running == 0
