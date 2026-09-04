"""The job layer: what the executor accepts, what it refuses, and the shape it reports."""

from __future__ import annotations

from datetime import UTC, datetime
from typing import Any
from uuid import uuid4

import pytest

from collector.__main__ import build_registries
from collector.icmp import IcmpExecutor, IcmpJobError
from collector.icmp.probe import IcmpUnavailableError, ProbeOutcome, ProbeReply
from collector.models import JobDevice, JobKind, LeasedJob

ADDRESS = "192.0.2.10"


def _job(parameters: dict[str, Any] | None, with_device: bool = True) -> LeasedJob:
    return LeasedJob(
        job_id=uuid4(),
        kind=JobKind.POLL,
        lease_token="lease-token",
        lease_expires_at=datetime.now(UTC),
        attempt=1,
        device=JobDevice(
            device_id=uuid4(),
            hostname="switch-01",
            ip_address=ADDRESS,
            vendor="CiscoIosXe",
        )
        if with_device
        else None,
        parameters=parameters,
    )


def _parameters(probe: str = "icmp") -> dict[str, Any]:
    return {"probe": probe, "count": 4, "timeoutSeconds": 2.0, "intervalSeconds": 0.25}


def _outcome(*round_trips: float | None) -> ProbeOutcome:
    replies = tuple(ProbeReply(sequence, rtt) for sequence, rtt in enumerate(round_trips))

    return ProbeOutcome(
        address=ADDRESS,
        sent=len(replies),
        received=sum(1 for reply in replies if reply.rtt_milliseconds is not None),
        replies=replies,
    )


@pytest.fixture
def probed(monkeypatch: pytest.MonkeyPatch) -> list[dict[str, Any]]:
    """Replaces the socket layer, recording what it was asked for."""
    calls: list[dict[str, Any]] = []

    async def fake_probe(
        address: str,
        count: int,
        reply_timeout_seconds: float,
        interval_seconds: float,
    ) -> ProbeOutcome:
        calls.append(
            {
                "address": address,
                "count": count,
                "reply_timeout_seconds": reply_timeout_seconds,
                "interval_seconds": interval_seconds,
            }
        )

        return _outcome(1.5, None, 2.5, 3.5)

    monkeypatch.setattr("collector.icmp.executor.probe", fake_probe)

    return calls


async def test_the_executor_answers_for_poll_jobs() -> None:
    # Poll is the kind ARCHITECTURE.md §7 already has for reading from a device, and
    # CollectorJobKind.Poll has named ICMP reachability since WP-1.3. No enum member was added.
    assert IcmpExecutor().kind is JobKind.POLL


async def test_the_icmp_executor_is_registered_by_the_entry_point() -> None:
    # The seam WP-1.3 shipped empty. Until this, every leased job was reported as a failure
    # naming the absence of an executor for its kind.
    executors, _ = build_registries()

    assert executors.for_kind(JobKind.POLL) is not None
    assert isinstance(executors.for_kind(JobKind.POLL), IcmpExecutor)


async def test_the_job_parameters_reach_the_probe(probed: list[dict[str, Any]]) -> None:
    # The API owns scheduling (ARCHITECTURE.md §7), so count, timeout and interval come down in
    # the job rather than being configured on this side.
    await IcmpExecutor().execute(_job(_parameters()))

    assert probed == [
        {
            "address": ADDRESS,
            "count": 4,
            "reply_timeout_seconds": 2.0,
            "interval_seconds": 0.25,
        }
    ]


async def test_the_result_carries_the_names_the_api_reads(probed: list[dict[str, Any]]) -> None:
    result = await IcmpExecutor().execute(_job(_parameters()))

    assert result["probe"] == "icmp"
    assert result["address"] == ADDRESS
    assert result["sent"] == 4
    assert result["received"] == 3
    assert result["lossPercent"] == 25.0
    assert result["rttMillisecondsMin"] == 1.5
    assert result["rttMillisecondsMax"] == 3.5
    assert result["rttMillisecondsAvg"] == pytest.approx(2.5)


async def test_the_result_carries_a_round_trip_for_every_request_sent(
    probed: list[dict[str, Any]],
) -> None:
    # "RTT is recorded per probe": one entry per request, in order, and an unanswered request is
    # present with a null rather than absent.
    result = await IcmpExecutor().execute(_job(_parameters()))

    assert result["replies"] == [
        {"sequence": 0, "rttMilliseconds": 1.5},
        {"sequence": 1, "rttMilliseconds": None},
        {"sequence": 2, "rttMilliseconds": 2.5},
        {"sequence": 3, "rttMilliseconds": 3.5},
    ]


async def test_a_probe_that_heard_nothing_succeeds_with_total_loss_and_no_round_trips(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    # The other half of the distinction this package rests on: a device that said nothing is a
    # successful job. Only an inability to probe is a failed one.
    async def silent(_address: str, **_: Any) -> ProbeOutcome:
        return _outcome(None, None)

    monkeypatch.setattr("collector.icmp.executor.probe", silent)

    result = await IcmpExecutor().execute(_job(_parameters()))

    assert result["received"] == 0
    assert result["lossPercent"] == 100.0
    assert result["rttMillisecondsMin"] is None
    assert result["rttMillisecondsAvg"] is None
    assert result["rttMillisecondsMax"] is None


async def test_an_unopenable_socket_is_raised_rather_than_reported_as_loss(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    # Raising is how a job is failed. A failed job leaves the device's state untouched, which is
    # what stops a collector that lost CAP_NET_RAW from taking the estate offline.
    async def unavailable(_address: str, **_: Any) -> ProbeOutcome:
        raise IcmpUnavailableError("no socket")

    monkeypatch.setattr("collector.icmp.executor.probe", unavailable)

    with pytest.raises(IcmpUnavailableError):
        await IcmpExecutor().execute(_job(_parameters()))


async def test_a_job_with_no_device_is_refused() -> None:
    with pytest.raises(IcmpJobError, match="names none"):
        await IcmpExecutor().execute(_job(_parameters(), with_device=False))


async def test_a_poll_job_with_no_parameters_is_refused() -> None:
    with pytest.raises(IcmpJobError, match="no parameters"):
        await IcmpExecutor().execute(_job(None))


async def test_a_poll_job_for_another_probe_is_refused_by_name() -> None:
    # A Poll queued by the SNMP metric polling in Phase 3 will look identical from the outside.
    # This side refuses it rather than answering it wrongly, and the API's result handler
    # likewise reads only the rows whose parameters say icmp.
    with pytest.raises(IcmpJobError, match="snmp"):
        await IcmpExecutor().execute(_job(_parameters(probe="snmp")))


async def test_parameters_that_are_not_a_probes_are_refused() -> None:
    with pytest.raises(IcmpJobError, match="not an ICMP probe"):
        await IcmpExecutor().execute(_job({"probe": "icmp", "count": "four"}))
