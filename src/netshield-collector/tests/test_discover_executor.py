"""The Discover executor: how a job finds its walk, and what happens when it names none.

``ExecutorRegistry`` is keyed by job kind and holds one executor per kind. Since WP-1.6 a
``Discover`` is two different pieces of work, so the discrimination happens a level down on the
``walk`` member — the same member the API's own result handlers filter on. These are the tests
that the level down behaves the way the level above does: an unrecognised walk is a failure
naming the reason, not a job answered wrongly.
"""

from __future__ import annotations

from typing import Any

import pytest

from collector.discovery import (
    SWEEP_NAME,
    DiscoverExecutor,
    DiscoveryJobError,
    RangeSweepExecutor,
)
from collector.icmp.probe import ProbeOutcome, ProbeReply
from collector.jobs import ExecutorRegistry
from collector.models import JobKind, LeasedJob
from collector.snmp.executor import WALK_NAME
from tests.conftest import sweep_job, walk_job


class _StubWalk:
    """A walk that records the job it was given, so dispatch can be observed."""

    def __init__(self, name: str) -> None:
        self.walk = name
        self.jobs: list[LeasedJob] = []

    async def execute(self, job: LeasedJob) -> dict[str, Any]:
        self.jobs.append(job)

        return {"walk": self.walk}


# --- Dispatch ----------------------------------------------------------------------------------


def test_the_discover_executor_answers_for_the_discover_kind() -> None:
    """One executor for the kind, which is what ExecutorRegistry will accept."""
    assert DiscoverExecutor([]).kind is JobKind.DISCOVER


def test_the_registry_accepts_it_beside_the_icmp_executor() -> None:
    registry = ExecutorRegistry([DiscoverExecutor([RangeSweepExecutor()])])

    assert registry.for_kind(JobKind.DISCOVER) is not None
    assert registry.for_kind(JobKind.POLL) is None


async def test_a_job_is_handed_to_the_walk_its_parameters_name() -> None:
    sweep = _StubWalk(SWEEP_NAME)
    snmp = _StubWalk(WALK_NAME)
    executor = DiscoverExecutor([sweep, snmp])

    job = sweep_job()

    await executor.execute(job)

    assert sweep.jobs == [job]
    assert snmp.jobs == []


async def test_a_fingerprint_job_and_a_sweep_job_reach_different_walks() -> None:
    """The two sit in the same table looking identical, and each side must recognise its own."""
    sweep = _StubWalk(SWEEP_NAME)
    snmp = _StubWalk(WALK_NAME)
    executor = DiscoverExecutor([sweep, snmp])

    await executor.execute(walk_job())
    await executor.execute(sweep_job())

    assert [job.parameters["walk"] for job in snmp.jobs if job.parameters] == [WALK_NAME]
    assert [job.parameters["walk"] for job in sweep.jobs if job.parameters] == [SWEEP_NAME]


async def test_a_walk_this_build_does_not_have_is_refused_with_the_reason() -> None:
    executor = DiscoverExecutor([_StubWalk(SWEEP_NAME)])

    with pytest.raises(DiscoveryJobError, match="lldp"):
        await executor.execute(sweep_job(parameters={"walk": "lldp"}))


async def test_a_job_with_no_parameters_is_refused() -> None:
    executor = DiscoverExecutor([_StubWalk(SWEEP_NAME)])

    job = LeasedJob.model_validate(
        {
            **sweep_job().model_dump(by_alias=True, mode="json"),
            "parameters": None,
        }
    )

    with pytest.raises(DiscoveryJobError, match="no parameters"):
        await executor.execute(job)


async def test_parameters_that_name_no_walk_are_refused() -> None:
    executor = DiscoverExecutor([_StubWalk(SWEEP_NAME)])

    with pytest.raises(DiscoveryJobError, match="do not name a walk"):
        await executor.execute(sweep_job(parameters={"firstAddress": "192.0.2.1"}))


def test_registering_a_walk_twice_is_a_mistake_rather_than_a_merge() -> None:
    with pytest.raises(ValueError, match=SWEEP_NAME):
        DiscoverExecutor([_StubWalk(SWEEP_NAME), _StubWalk(SWEEP_NAME)])


# --- The sweep walk ----------------------------------------------------------------------------


def _probes(monkeypatch: pytest.MonkeyPatch, answering: dict[str, float]) -> None:
    async def fake(address: str, **_: Any) -> ProbeOutcome:
        rtt = answering.get(address)

        return ProbeOutcome(
            address=address,
            sent=1,
            received=1 if rtt is not None else 0,
            replies=(ProbeReply(sequence=0, rtt_milliseconds=rtt),),
        )

    monkeypatch.setattr("collector.discovery.sweep.probe", fake)


async def test_the_sweep_walk_reports_the_payload_the_api_reads(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Every member is named the way the API's own RangeSweepResult declares it."""
    _probes(monkeypatch, {"192.0.2.2": 3.5})

    payload = await RangeSweepExecutor().execute(
        sweep_job(first="192.0.2.1", last="192.0.2.4", exclusions=["192.0.2.4"])
    )

    assert payload == {
        "walk": "sweep",
        "firstAddress": "192.0.2.1",
        "lastAddress": "192.0.2.4",
        "scanned": 3,
        "excluded": 1,
        "truncated": False,
        "responders": [{"address": "192.0.2.2", "rttMilliseconds": 3.5}],
    }


async def test_the_sweep_walk_needs_no_device_and_no_credential(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """The whole point of a sweep: it is looking for hosts that are not devices yet."""
    _probes(monkeypatch, {})

    job = sweep_job()

    assert job.device is None
    assert job.credential is None

    await RangeSweepExecutor().execute(job)


async def test_the_sweep_walk_refuses_a_job_that_names_another_walk() -> None:
    """It does not trust its dispatcher: a walk registered under the wrong name must not answer."""
    with pytest.raises(DiscoveryJobError, match="snmp"):
        await RangeSweepExecutor().execute(walk_job())


async def test_the_sweep_walk_refuses_parameters_that_are_not_a_sweep_s() -> None:
    with pytest.raises(DiscoveryJobError, match="not a range sweep"):
        await RangeSweepExecutor().execute(
            sweep_job(parameters={"walk": "sweep", "firstAddress": "192.0.2.1"})
        )


async def test_the_sweep_walk_refuses_a_job_with_no_parameters() -> None:
    job = LeasedJob.model_validate(
        {
            **sweep_job().model_dump(by_alias=True, mode="json"),
            "parameters": None,
        }
    )

    with pytest.raises(DiscoveryJobError, match="no parameters"):
        await RangeSweepExecutor().execute(job)
