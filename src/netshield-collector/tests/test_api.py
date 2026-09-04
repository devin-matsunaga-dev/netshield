"""The client for the internal contract: what it sends, and how it behaves when the API does not
answer."""

from __future__ import annotations

from datetime import UTC, datetime
from uuid import uuid4

import httpx
import pytest
import respx

from collector.api import CollectorApi, CollectorApiError
from collector.config import CollectorSettings
from collector.models import JobKind, JobOutcome, ResultReport
from tests.conftest import SHARED_SECRET


async def test_lease_presents_the_shared_secret_as_a_bearer_token(
    settings: CollectorSettings,
    api_mock: respx.MockRouter,
) -> None:
    route = api_mock.get("/internal/collector/jobs").mock(
        return_value=httpx.Response(200, json={"jobs": [], "leaseSeconds": 300})
    )

    async with CollectorApi(settings) as api:
        await api.lease_jobs(limit=5)

    assert route.calls.last.request.headers["Authorization"] == f"Bearer {SHARED_SECRET}"


async def test_lease_names_the_collector_and_the_limit(
    settings: CollectorSettings,
    api_mock: respx.MockRouter,
) -> None:
    route = api_mock.get("/internal/collector/jobs").mock(
        return_value=httpx.Response(200, json={"jobs": [], "leaseSeconds": 300})
    )

    async with CollectorApi(settings) as api:
        batch = await api.lease_jobs(limit=3)

    assert batch.jobs == []
    assert batch.lease_seconds == 300
    assert route.calls.last.request.url.params["collector"] == "collector-test"
    assert route.calls.last.request.url.params["limit"] == "3"


async def test_a_leased_job_with_a_credential_is_parsed(
    settings: CollectorSettings,
    api_mock: respx.MockRouter,
) -> None:
    job_id = str(uuid4())

    api_mock.get("/internal/collector/jobs").mock(
        return_value=httpx.Response(
            200,
            json={
                "leaseSeconds": 300,
                "jobs": [
                    {
                        "jobId": job_id,
                        "kind": "Discover",
                        "leaseToken": "token-1",
                        "leaseExpiresAt": datetime.now(UTC).isoformat(),
                        "attempt": 1,
                        "device": {
                            "deviceId": str(uuid4()),
                            "hostname": "core-sw-1",
                            "ipAddress": "10.0.0.1",
                            "vendor": "CiscoIos",
                        },
                        "credential": {
                            "credentialProfileId": str(uuid4()),
                            "kind": "SnmpV2c",
                            "material": {"community": "public"},
                        },
                    }
                ],
            },
        )
    )

    async with CollectorApi(settings) as api:
        batch = await api.lease_jobs(limit=1)

    job = batch.jobs[0]

    assert job.kind is JobKind.DISCOVER
    assert job.device is not None
    assert job.device.hostname == "core-sw-1"
    assert job.credential is not None

    # It arrived, and it is still not printable.
    assert job.credential.material.community is not None
    assert job.credential.material.community.get_secret_value() == "public"
    assert "public" not in repr(job)


async def test_submit_sends_the_batch_and_reads_the_acknowledgement(
    settings: CollectorSettings,
    api_mock: respx.MockRouter,
) -> None:
    job_id = uuid4()

    route = api_mock.post("/internal/collector/results").mock(
        return_value=httpx.Response(
            200,
            json={"accepted": [str(job_id)], "duplicates": [], "rejected": []},
        )
    )

    report = ResultReport(
        job_id=job_id,
        lease_token="token-1",
        outcome=JobOutcome.SUCCEEDED,
        data={"reachable": True},
    )

    async with CollectorApi(settings) as api:
        ack = await api.submit_results([report])

    assert ack.accepted == [job_id]

    sent = route.calls.last.request
    assert b'"collector":"collector-test"' in sent.content
    assert b'"leaseToken":"token-1"' in sent.content


async def test_heartbeat_reports_capacity_and_adopts_the_answer(
    settings: CollectorSettings,
    api_mock: respx.MockRouter,
) -> None:
    route = api_mock.post("/internal/collector/heartbeat").mock(
        return_value=httpx.Response(
            200,
            json={
                "acknowledgedAt": datetime.now(UTC).isoformat(),
                "pollSeconds": 20,
                "leaseSeconds": 300,
                "maxJobsPerLease": 25,
            },
        )
    )

    async with CollectorApi(settings) as api:
        ack = await api.heartbeat(running=2)

    assert ack.poll_seconds == 20
    assert ack.max_jobs_per_lease == 25
    assert b'"running":2' in route.calls.last.request.content
    assert b'"capacity":4' in route.calls.last.request.content


async def test_a_transport_failure_is_retried_then_reported(
    settings: CollectorSettings,
    api_mock: respx.MockRouter,
) -> None:
    route = api_mock.post("/internal/collector/heartbeat").mock(
        side_effect=httpx.ConnectError("connection refused")
    )

    async with CollectorApi(settings) as api:
        with pytest.raises(CollectorApiError):
            await api.heartbeat(running=0)

    assert route.call_count == settings.max_attempts


async def test_a_503_is_retried_and_the_next_answer_is_taken(
    settings: CollectorSettings,
    api_mock: respx.MockRouter,
) -> None:
    api_mock.get("/internal/collector/jobs").mock(
        side_effect=[
            httpx.Response(503),
            httpx.Response(200, json={"jobs": [], "leaseSeconds": 300}),
        ]
    )

    async with CollectorApi(settings) as api:
        batch = await api.lease_jobs(limit=1)

    assert batch.lease_seconds == 300


async def test_a_401_is_not_retried(
    settings: CollectorSettings,
    api_mock: respx.MockRouter,
) -> None:
    """A wrong shared secret is wrong however many times it is sent."""
    route = api_mock.get("/internal/collector/jobs").mock(return_value=httpx.Response(401))

    async with CollectorApi(settings) as api:
        with pytest.raises(CollectorApiError):
            await api.lease_jobs(limit=1)

    assert route.call_count == 1
