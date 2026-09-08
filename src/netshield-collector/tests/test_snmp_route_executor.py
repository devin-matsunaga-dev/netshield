"""The route walk end to end, and the failure isolation that is the reason it is its own walk."""

from __future__ import annotations

from collections.abc import AsyncIterator, Sequence
from contextlib import asynccontextmanager
from typing import Any

import pytest

from collector.models import CredentialKind, LeasedJob
from collector.snmp.routes import RouteWalkExecutor, RouteWalkJobError
from collector.snmp.session import FixtureSession, SnmpError, SnmpSession
from tests.conftest import route_job, snmp_credential, topology_walk_fixture


def session_factory(name: str, *, seen: list[Any] | None = None) -> Any:
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


# --- the payload -----------------------------------------------------------------------------


async def test_execute_reduces_a_routing_table_to_its_gateways() -> None:
    payload = await RouteWalkExecutor(session_factory("router_routes")).execute(route_job())

    assert payload["walk"] == "routes"
    assert payload["routesSupported"] is True
    assert payload["routeTable"] == "inetCidrRoute"
    assert payload["routeCount"] == 5
    assert payload["nextHopCount"] == 3


async def test_execute_writes_every_member_the_api_reads() -> None:
    # The payload's field names are the contract. This is the test that fails when one moves.
    payload = await RouteWalkExecutor(session_factory("router_routes")).execute(route_job())

    assert set(payload) == {
        "walk",
        "routesSupported",
        "routeTable",
        "routeCount",
        "nextHopCount",
        "nextHopsTruncated",
        "nextHops",
    }

    assert set(payload["nextHops"][0]) == {
        "address",
        "ifIndex",
        "localPortName",
        "routeCount",
    }


async def test_execute_carries_the_route_count_that_tells_a_default_from_a_stub() -> None:
    payload = await RouteWalkExecutor(session_factory("router_routes")).execute(route_job())

    assert payload["nextHops"][0] == {
        "address": "192.0.2.11",
        "ifIndex": 1,
        "localPortName": "Et1",
        "routeCount": 2,
    }


async def test_execute_against_a_switch_with_no_routing_table_reports_unsupported() -> None:
    payload = await RouteWalkExecutor(session_factory("no_routes")).execute(route_job())

    assert payload["routesSupported"] is False
    assert payload["nextHops"] == []
    assert payload["routeTable"] is None


# --- failure isolation -----------------------------------------------------------------------


async def test_a_routing_table_that_never_answers_fails_only_this_job() -> None:
    """The whole reason routes are a separate walk from LLDP and CDP.

    A device that answers its neighbour protocols and then dies partway through forty thousand
    routes fails *this* job. The neighbour walk is a different row in ``collector_jobs`` with a
    different result, and the edges it established are untouched — where one combined walk would
    have discarded them.
    """

    class DyingSession:
        """Answers the interface columns and then stops, the way a real timeout arrives."""

        async def get(self, oids: Sequence[str]) -> dict[str, str]:
            return {}

        async def walk(self, root: str, *, max_rows: int) -> dict[str, str]:
            if root.startswith("1.3.6.1.2.1.4.24"):
                raise SnmpError("192.0.2.10 stopped answering partway through the route table.")

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
        await RouteWalkExecutor(factory).execute(route_job())


# --- the refusals ----------------------------------------------------------------------------


async def test_execute_with_no_device_fails_the_job() -> None:
    job = LeasedJob.model_validate(
        route_job().model_dump(mode="json", by_alias=True) | {"device": None}
    )

    with pytest.raises(RouteWalkJobError, match="needs a device"):
        await RouteWalkExecutor(session_factory("router_routes")).execute(job)


async def test_execute_with_no_credential_fails_the_job() -> None:
    job = LeasedJob.model_validate(
        route_job().model_dump(mode="json", by_alias=True) | {"credential": None}
    )

    with pytest.raises(RouteWalkJobError, match="needs a credential"):
        await RouteWalkExecutor(session_factory("router_routes")).execute(job)


async def test_execute_with_another_walks_parameters_refuses_rather_than_reading_anything() -> None:
    job = route_job(
        parameters={
            "walk": "neighbors",
            "timeoutSeconds": 2.0,
            "retries": 1,
            "maxRepetitions": 25,
            "maxRows": 5000,
            "maxNextHops": 500,
        }
    )

    with pytest.raises(RouteWalkJobError, match="names neighbors"):
        await RouteWalkExecutor(session_factory("router_routes")).execute(job)


async def test_execute_with_parameters_of_the_wrong_shape_refuses_at_once() -> None:
    job = route_job(parameters={"walk": "routes", "maxNextHops": 0})

    with pytest.raises(RouteWalkJobError, match="not a route walk"):
        await RouteWalkExecutor(session_factory("router_routes")).execute(job)


async def test_execute_with_no_parameters_fails_the_job() -> None:
    job = LeasedJob.model_validate(
        route_job().model_dump(mode="json", by_alias=True) | {"parameters": None}
    )

    with pytest.raises(RouteWalkJobError, match="no parameters"):
        await RouteWalkExecutor(session_factory("router_routes")).execute(job)


# --- the lease -------------------------------------------------------------------------------


async def test_execute_reads_with_the_credential_the_lease_delivered() -> None:
    seen: list[Any] = []
    credential = snmp_credential(kind=CredentialKind.SNMP_V2C)

    await RouteWalkExecutor(session_factory("router_routes", seen=seen)).execute(
        route_job(credential=credential, address="198.51.100.7")
    )

    assert seen == [("198.51.100.7", credential, 2.0, 1, 25)]


async def test_execute_declares_the_discriminator_the_api_writes() -> None:
    assert RouteWalkExecutor().walk == "routes"
