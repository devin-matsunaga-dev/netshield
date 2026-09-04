"""The loop: what it does with a job it can run, one it cannot, and one that misbehaves."""

from __future__ import annotations

import asyncio
from datetime import UTC, datetime
from typing import Any
from uuid import uuid4

import httpx
import respx

from collector.api import CollectorApi
from collector.config import CollectorSettings
from collector.jobs import NO_EXECUTOR, ExecutorRegistry
from collector.models import JobKind, LeasedJob
from collector.runner import CollectorRunner


class RecordingExecutor:
    """An executor that says yes, and remembers what it was handed."""

    kind = JobKind.POLL

    def __init__(self) -> None:
        self.seen: list[LeasedJob] = []

    async def execute(self, job: LeasedJob) -> dict[str, Any]:
        self.seen.append(job)

        return {"reachable": True}


class HangingExecutor:
    """An executor that never returns — the failure CONVENTIONS.md §5 requires a timeout for."""

    kind = JobKind.POLL

    async def execute(self, job: LeasedJob) -> dict[str, Any]:
        await asyncio.sleep(3600)

        raise AssertionError("unreachable")


class LeakingExecutor:
    """An executor whose exception message quotes the credential it was given."""

    kind = JobKind.POLL

    async def execute(self, job: LeasedJob) -> dict[str, Any]:
        raise RuntimeError("snmp auth failed (community=s3cr3t)")


def _job_payload(kind: JobKind = JobKind.POLL, token: str = "token-1") -> dict[str, Any]:
    return {
        "jobId": str(uuid4()),
        "kind": kind.value,
        "leaseToken": token,
        "leaseExpiresAt": datetime.now(UTC).isoformat(),
        "attempt": 1,
    }


def _mock_lease(api_mock: respx.MockRouter, jobs: list[dict[str, Any]]) -> None:
    api_mock.get("/internal/collector/jobs").mock(
        return_value=httpx.Response(200, json={"jobs": jobs, "leaseSeconds": 300})
    )


def _mock_submit(api_mock: respx.MockRouter) -> respx.Route:
    return api_mock.post("/internal/collector/results").mock(
        return_value=httpx.Response(200, json={"accepted": [], "duplicates": [], "rejected": []})
    )


async def test_a_job_is_executed_and_reported(
    settings: CollectorSettings,
    api_mock: respx.MockRouter,
) -> None:
    _mock_lease(api_mock, [_job_payload()])
    submit = _mock_submit(api_mock)

    executor = RecordingExecutor()

    async with CollectorApi(settings) as api:
        await CollectorRunner(api, ExecutorRegistry([executor]), settings).work_once()

    assert len(executor.seen) == 1
    assert b'"outcome":"Succeeded"' in submit.calls.last.request.content
    assert b'"reachable":true' in submit.calls.last.request.content


async def test_the_lease_token_is_returned_with_the_result(
    settings: CollectorSettings,
    api_mock: respx.MockRouter,
) -> None:
    """It is what makes the API's idempotency work, so the collector must echo it exactly."""
    _mock_lease(api_mock, [_job_payload(token="token-42")])
    submit = _mock_submit(api_mock)

    async with CollectorApi(settings) as api:
        await CollectorRunner(api, ExecutorRegistry([RecordingExecutor()]), settings).work_once()

    assert b'"leaseToken":"token-42"' in submit.calls.last.request.content


async def test_a_kind_with_no_executor_is_reported_as_a_failure(
    settings: CollectorSettings,
    api_mock: respx.MockRouter,
) -> None:
    """The state every WP-1.3 build is in: no executors registered at all."""
    _mock_lease(api_mock, [_job_payload(kind=JobKind.CONFIG_FETCH)])
    submit = _mock_submit(api_mock)

    async with CollectorApi(settings) as api:
        await CollectorRunner(api, ExecutorRegistry(), settings).work_once()

    body = submit.calls.last.request.content

    assert b'"outcome":"Failed"' in body
    assert NO_EXECUTOR.encode() in body


async def test_a_hanging_job_times_out_rather_than_wedging_the_pool(
    settings: CollectorSettings,
    api_mock: respx.MockRouter,
) -> None:
    _mock_lease(api_mock, [_job_payload()])
    submit = _mock_submit(api_mock)

    async with CollectorApi(settings) as api:
        runner = CollectorRunner(api, ExecutorRegistry([HangingExecutor()]), settings)

        await asyncio.wait_for(runner.work_once(), timeout=5)

    assert b'"outcome":"Failed"' in submit.calls.last.request.content
    assert runner.running == 0


async def test_a_failing_job_does_not_stop_the_others(
    settings: CollectorSettings,
    api_mock: respx.MockRouter,
) -> None:
    _mock_lease(api_mock, [_job_payload(), _job_payload(kind=JobKind.DISCOVER)])
    submit = _mock_submit(api_mock)

    async with CollectorApi(settings) as api:
        await CollectorRunner(api, ExecutorRegistry([RecordingExecutor()]), settings).work_once()

    body = submit.calls.last.request.content

    assert b'"outcome":"Succeeded"' in body
    assert b'"outcome":"Failed"' in body


async def test_a_credential_in_an_executors_error_is_not_submitted(
    settings: CollectorSettings,
    api_mock: respx.MockRouter,
) -> None:
    """A device error message is exactly where a community string ends up (SPEC.md §5)."""
    _mock_lease(api_mock, [_job_payload()])
    submit = _mock_submit(api_mock)

    async with CollectorApi(settings) as api:
        await CollectorRunner(api, ExecutorRegistry([LeakingExecutor()]), settings).work_once()

    assert b"s3cr3t" not in submit.calls.last.request.content


async def test_an_empty_batch_submits_nothing(
    settings: CollectorSettings,
    api_mock: respx.MockRouter,
) -> None:
    _mock_lease(api_mock, [])
    submit = _mock_submit(api_mock)

    async with CollectorApi(settings) as api:
        await CollectorRunner(api, ExecutorRegistry(), settings).work_once()

    assert submit.call_count == 0


async def test_no_more_is_leased_than_there_is_room_for(
    settings: CollectorSettings,
    api_mock: respx.MockRouter,
) -> None:
    lease = api_mock.get("/internal/collector/jobs").mock(
        return_value=httpx.Response(200, json={"jobs": [], "leaseSeconds": 300})
    )

    async with CollectorApi(settings) as api:
        await CollectorRunner(api, ExecutorRegistry(), settings).work_once()

    assert lease.calls.last.request.url.params["limit"] == str(settings.capacity)


async def test_the_heartbeat_answer_replaces_the_configured_pacing(
    settings: CollectorSettings,
    api_mock: respx.MockRouter,
) -> None:
    """ARCHITECTURE.md §7: the API owns scheduling, and the collector adopts what it is told."""
    _mock_lease(api_mock, [])
    api_mock.post("/internal/collector/heartbeat").mock(
        return_value=httpx.Response(
            200,
            json={
                "acknowledgedAt": datetime.now(UTC).isoformat(),
                "pollSeconds": 42,
                "leaseSeconds": 300,
                "maxJobsPerLease": 2,
            },
        )
    )

    async with CollectorApi(settings) as api:
        runner = CollectorRunner(api, ExecutorRegistry(), settings)

        stopper = asyncio.create_task(_stop_soon(runner))
        await runner.run()
        await stopper

    assert runner._poll_seconds == 42.0


async def _stop_soon(runner: CollectorRunner) -> None:
    await asyncio.sleep(0.05)
    runner.stop()


async def test_an_unreachable_api_does_not_stop_the_loop(
    settings: CollectorSettings,
    api_mock: respx.MockRouter,
) -> None:
    """A degraded API is self-healing: the leases expire and the queue keeps the work."""
    api_mock.get("/internal/collector/jobs").mock(side_effect=httpx.ConnectError("refused"))
    api_mock.post("/internal/collector/heartbeat").mock(side_effect=httpx.ConnectError("refused"))

    async with CollectorApi(settings) as api:
        runner = CollectorRunner(api, ExecutorRegistry(), settings)

        stopper = asyncio.create_task(_stop_soon(runner))
        await runner.run()
        await stopper
