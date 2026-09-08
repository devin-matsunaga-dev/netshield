"""The client walk end to end: a leased job in, the API's payload shape out.

The executor itself does very little — it validates parameters, opens one session, reads two
tables and writes the payload — so what these tests are really pinning is the payload's field
names and the refusals. The names matter because the two sides of that shape live in two
repositories with no generator between them: a member renamed here should break a test rather
than quietly stop being read by ``RecordClientWalkResultHandler``.
"""

from __future__ import annotations

from collections.abc import AsyncIterator
from contextlib import asynccontextmanager
from typing import Any

import pytest

from collector.models import CredentialKind, LeasedJob
from collector.snmp.clients import ClientWalkExecutor, ClientWalkJobError
from collector.snmp.session import FixtureSession, SnmpSession
from tests.conftest import client_job, client_walk_fixture, snmp_credential


def session_factory(name: str) -> Any:
    """A session factory replaying one recorded client walk, ignoring the address."""
    values = client_walk_fixture(name)

    @asynccontextmanager
    async def factory(
        address: str,
        credential: Any,
        *,
        timeout_seconds: float,
        retries: int,
        max_repetitions: int,
    ) -> AsyncIterator[SnmpSession]:
        yield FixtureSession(values)

    return factory


async def test_execute_against_a_layer3_switch_reports_both_tables() -> None:
    executor = ClientWalkExecutor(session_factory("layer3_switch"))

    payload = await executor.execute(client_job())

    assert payload["walk"] == "clients"
    assert payload["neighborsSupported"] is True
    assert payload["forwardingSupported"] is True
    assert payload["neighborCount"] == 3
    assert payload["forwardingCount"] == 7


async def test_execute_writes_every_member_the_api_reads() -> None:
    # The payload's field names are the contract. This is the test that fails when one moves.
    executor = ClientWalkExecutor(session_factory("layer3_switch"))

    payload = await executor.execute(client_job())

    assert set(payload) == {
        "walk",
        "neighborsSupported",
        "neighborCount",
        "neighborsTruncated",
        "neighbors",
        "forwardingSupported",
        "forwardingCount",
        "forwardingTruncated",
        "forwarding",
    }
    assert set(payload["neighbors"][0]) == {"ipAddress", "macAddress", "ifIndex"}
    assert set(payload["forwarding"][0]) == {
        "macAddress",
        "ifIndex",
        "vlanId",
        "macCountOnPort",
    }


async def test_execute_against_a_router_reports_forwarding_unsupported() -> None:
    executor = ClientWalkExecutor(session_factory("router"))

    payload = await executor.execute(client_job())

    assert payload["neighborsSupported"] is True
    assert payload["forwardingSupported"] is False
    assert payload["forwarding"] == []


async def test_execute_against_a_device_with_neither_table_reports_both_unsupported() -> None:
    executor = ClientWalkExecutor(session_factory("no_client_tables"))

    payload = await executor.execute(client_job())

    assert payload["neighborsSupported"] is False
    assert payload["forwardingSupported"] is False


async def test_execute_with_no_device_fails_the_job() -> None:
    executor = ClientWalkExecutor(session_factory("layer3_switch"))

    job = LeasedJob.model_validate(
        client_job().model_dump(by_alias=True) | {"device": None},
    )

    with pytest.raises(ClientWalkJobError, match="needs a device"):
        await executor.execute(job)


async def test_execute_with_no_credential_fails_the_job() -> None:
    executor = ClientWalkExecutor(session_factory("layer3_switch"))

    job = LeasedJob.model_validate(client_job().model_dump(by_alias=True) | {"credential": None})

    with pytest.raises(ClientWalkJobError, match="needs a credential"):
        await executor.execute(job)


async def test_execute_with_another_walks_parameters_refuses_rather_than_reading_anything() -> None:
    # A walk that trusted its dispatcher would answer the wrong question the first time somebody
    # registered it under the wrong name.
    executor = ClientWalkExecutor(session_factory("layer3_switch"))
    parameters = dict(client_job().parameters or {}) | {"walk": "snmp"}

    with pytest.raises(ClientWalkJobError, match="names snmp"):
        await executor.execute(client_job(parameters=parameters))


async def test_execute_with_parameters_of_the_wrong_shape_refuses_at_once() -> None:
    executor = ClientWalkExecutor(session_factory("layer3_switch"))

    with pytest.raises(ClientWalkJobError, match="not a client walk"):
        await executor.execute(client_job(parameters={"walk": "clients"}))


async def test_execute_with_no_parameters_fails_the_job() -> None:
    executor = ClientWalkExecutor(session_factory("layer3_switch"))

    job = LeasedJob.model_validate(client_job().model_dump(by_alias=True) | {"parameters": None})

    with pytest.raises(ClientWalkJobError, match="no parameters"):
        await executor.execute(job)


async def test_execute_reads_with_the_credential_the_lease_delivered() -> None:
    # The credential is not this walk's to interpret — the session decides what an SNMP
    # credential is, and test_snmp_session.py is where that refusal lives. What matters here is
    # that the one the lease carried is the one handed over, unmodified.
    seen: list[Any] = []
    values = client_walk_fixture("layer3_switch")

    @asynccontextmanager
    async def factory(
        address: str,
        credential: Any,
        *,
        timeout_seconds: float,
        retries: int,
        max_repetitions: int,
    ) -> AsyncIterator[SnmpSession]:
        seen.append((address, credential, timeout_seconds, retries, max_repetitions))

        yield FixtureSession(values)

    credential = snmp_credential(kind=CredentialKind.SNMP_V2C)

    await ClientWalkExecutor(factory).execute(
        client_job(credential=credential, address="198.51.100.7")
    )

    assert seen == [("198.51.100.7", credential, 2.0, 1, 25)]


async def test_execute_declares_the_discriminator_the_api_writes() -> None:
    assert ClientWalkExecutor().walk == "clients"
